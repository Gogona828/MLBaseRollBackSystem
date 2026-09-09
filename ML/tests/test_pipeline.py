from __future__ import annotations

import unittest

import pandas as pd
import numpy as np

from action_prediction.active_inference import (
    active_inference_probabilities,
    fit_active_inference_model,
    infer_frame_beliefs,
)
from action_prediction.constants import action_id_to_class, action_id_to_state_group
from action_prediction.evaluation import evaluate_probabilities
from action_prediction.features import FEATURE_COLUMNS, build_feature_frame, build_supervised_dataset
from action_prediction.autoregressive import _run_xgboost_rollout, build_rollout_paths
from action_prediction.split import apply_session_split, make_session_split


def synthetic_frames() -> pd.DataFrame:
    rows = []
    actions = [0, 0, 0, 1, 1, 1, 100, 100, 0, 301, 301, 0, 200, 0, 2, 2]
    for session_index, session in enumerate(("s1", "s2", "s3", "s4", "s5", "s6")):
        for frame, action in enumerate(actions):
            rows.append(
                {
                    "match_id": f"m{session_index}",
                    "session_id": session,
                    "player_id": 1,
                    "frame": frame,
                    "fixed_delta_time": 1 / 60,
                    "input_bits": 0,
                    "input_left": False,
                    "input_right": False,
                    "input_attack": False,
                    "input_down_bits": 0,
                    "input_up_bits": 0,
                    "p1_position_x": float(frame),
                    "p1_position_y": 0.0,
                    "p1_velocity_x": 1.0,
                    "p1_is_face_right": True,
                    "p1_vital_health": 3,
                    "p1_guard_health": 3,
                    "p1_is_dead": False,
                    "p1_action_id": action,
                    "p1_action_frame": frame,
                    "p1_hitstun_frame": 0,
                    "p1_is_in_hitstun": False,
                    "p1_buffer_action_id": 115,
                    "p2_position_x": float(frame + 4),
                    "p2_position_y": 0.0,
                    "p2_velocity_x": 0.0,
                    "p2_is_face_right": False,
                    "p2_vital_health": 3,
                    "p2_guard_health": 3,
                    "p2_is_dead": False,
                    "p2_action_id": 0,
                    "p2_action_frame": frame,
                    "p2_hitstun_frame": 0,
                    "p2_is_in_hitstun": False,
                    "p2_buffer_action_id": 110,
                    "distance_x": 4.0,
                    "absolute_distance": 4.0,
                    "relative_velocity": -1.0,
                    "p1_is_cornered": False,
                    "p2_is_cornered": False,
                }
            )
    return pd.DataFrame(rows)


class PipelineTests(unittest.TestCase):
    def test_action_mapping(self) -> None:
        self.assertEqual(action_id_to_class(10), 1)
        self.assertEqual(action_id_to_class(115), 3)
        self.assertEqual(action_id_to_class(310), -1)
        self.assertEqual(action_id_to_state_group(200), 5)
        self.assertEqual(action_id_to_state_group(500), 6)

    def test_feature_generation_is_leakage_safe(self) -> None:
        features = build_feature_frame(synthetic_frames())
        self.assertNotIn("p1_buffer_action_id", FEATURE_COLUMNS)
        self.assertNotIn("p2_buffer_action_id", FEATURE_COLUMNS)
        row = features[(features["match_id"] == "m0") & (features["frame"] == 6)].iloc[0]
        self.assertEqual(row["previous_action_id_1"], 1)
        self.assertEqual(row["distance_3_frames_ago"], 4.0)

    def test_future_label_is_exact_and_forced_targets_are_dropped(self) -> None:
        features = build_feature_frame(synthetic_frames())
        supervised = build_supervised_dataset(features, 3)
        row = supervised[(supervised["match_id"] == "m0") & (supervised["frame"] == 3)].iloc[0]
        self.assertEqual(row["target_action_id"], 100)
        self.assertFalse(((supervised["match_id"] == "m0") & (supervised["frame"] == 9)).any())

    def test_session_split_has_no_overlap(self) -> None:
        frames = synthetic_frames()
        assignment = make_session_split(frames["session_id"], seed=7)
        split = apply_session_split(frames, assignment)
        sessions_by_split = {
            name: set(frames.loc[split == name, "session_id"])
            for name in ("train", "validation", "test")
        }
        self.assertTrue(sessions_by_split["train"].isdisjoint(sessions_by_split["validation"]))
        self.assertTrue(sessions_by_split["train"].isdisjoint(sessions_by_split["test"]))
        self.assertTrue(sessions_by_split["validation"].isdisjoint(sessions_by_split["test"]))

    def test_active_inference_probabilities_and_shapes(self) -> None:
        features = build_feature_frame(synthetic_frames())
        assignment = make_session_split(features["session_id"], seed=7)
        model = fit_active_inference_model(features, assignment, iterations=3)
        beliefs = infer_frame_beliefs(features, model)
        probabilities = active_inference_probabilities(model, beliefs, horizon_frames=6)
        self.assertEqual(model.observation_likelihood.shape, (5, 3))
        self.assertEqual(model.transition.shape, (3, 3))
        self.assertEqual(probabilities.shape, (len(features), 5))
        self.assertTrue(np.allclose(model.observation_likelihood.sum(axis=0), 1.0))
        self.assertTrue(np.allclose(model.transition.sum(axis=0), 1.0))
        self.assertTrue(np.allclose(probabilities.sum(axis=1), 1.0))
        self.assertTrue(np.allclose(beliefs.sum(axis=1), 1.0))

    def test_accuracy_is_split_by_actual_action_change(self) -> None:
        y_true = np.array([0, 1, 2, 3])
        current_actions = np.array([0, 0, 2, 4])
        predicted_classes = np.array([0, 2, 1, 3])
        probabilities = np.eye(5)[predicted_classes]

        metrics = evaluate_probabilities(y_true, probabilities, current_actions)

        self.assertEqual(metrics["changed_action_samples"], 2)
        self.assertEqual(metrics["unchanged_action_samples"], 2)
        self.assertEqual(metrics["changed_action_accuracy"], 0.5)
        self.assertEqual(metrics["unchanged_action_accuracy"], 0.5)

    def test_empty_action_change_group_has_no_accuracy(self) -> None:
        y_true = np.array([0, 1])
        probabilities = np.eye(5)[y_true]

        metrics = evaluate_probabilities(y_true, probabilities, y_true)

        self.assertEqual(metrics["changed_action_samples"], 0)
        self.assertIsNone(metrics["changed_action_accuracy"])
        self.assertEqual(metrics["unchanged_action_accuracy"], 1.0)

    def test_rollout_paths_start_at_every_possible_frame(self) -> None:
        features = build_feature_frame(synthetic_frames())
        assignment = {session: "test" for session in features["session_id"].unique()}

        paths = build_rollout_paths(features, assignment, horizon_frames=3)

        # Forced/unknown actions may occur inside a path, but both endpoints
        # must be one of the five evaluated voluntary classes.
        self.assertGreater(len(paths), 0)
        self.assertTrue(np.all(paths[:, -1] - paths[:, 0] == 3))
        start_classes = features.loc[paths[:, 0], "self_action_id"].map(action_id_to_class)
        target_classes = features.loc[paths[:, -1], "self_action_id"].map(action_id_to_class)
        self.assertTrue(start_classes.ge(0).all())
        self.assertTrue(target_classes.ge(0).all())

    def test_autoregressive_rollout_does_not_read_future_physical_features(self) -> None:
        class RecordingModel:
            classes_ = np.arange(5)

            def __init__(self) -> None:
                self.calls: list[np.ndarray] = []

            def predict_proba(self, values: np.ndarray) -> np.ndarray:
                self.calls.append(values.copy())
                probabilities = np.zeros((len(values), 5), dtype=float)
                probabilities[:, 0] = 1.0
                return probabilities

        features = build_feature_frame(synthetic_frames())
        assignment = {session: "test" for session in features["session_id"].unique()}
        path = build_rollout_paths(features, assignment, horizon_frames=3)[:1]
        model = RecordingModel()

        _run_xgboost_rollout(features, path, model, "50ms", 50)

        position_index = FEATURE_COLUMNS.index("self_position_x")
        action_index = FEATURE_COLUMNS.index("self_action_id")
        self.assertEqual(len(model.calls), 3)
        self.assertEqual(model.calls[0][0, position_index], model.calls[1][0, position_index])
        self.assertEqual(model.calls[1][0, action_index], 0)


if __name__ == "__main__":
    unittest.main()
