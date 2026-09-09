"""Scientific invariants for the new probability and temporal models."""
import unittest

import numpy as np

from action_prediction.active_inference import fit_active_inference_model, infer_frame_beliefs
from action_prediction.features import build_feature_frame
from action_prediction.hybrid_experiments import (
    belief_features, causal_age, compose_two_stage, fit_context_transitions,
    fit_duration, generative,
)
from test_pipeline import synthetic_frames


class HybridTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.features = build_feature_frame(synthetic_frames())
        cls.assignment = {f"s{i}": "train" if i < 4 else "test" for i in range(1, 7)}
        cls.train = cls.features.session_id.map(cls.assignment).eq("train").to_numpy()
        cls.model = fit_active_inference_model(cls.features, cls.assignment, iterations=3)
        cls.beliefs = infer_frame_beliefs(cls.features, cls.model)

    def test_generative_one_step_is_marginalization(self):
        b = np.array([[0.2, 0.3, 0.5]])
        expected = self.model.observation_likelihood @ self.model.transition @ b[0]
        np.testing.assert_allclose(generative(self.model, b, 1)[0], expected)

    def test_two_stage_limits_and_forced_current(self):
        dest = np.array([[.1, .2, .3, .2, .2]] * 3)
        p = compose_two_stage(np.array([0., 1., .2]), dest, np.array([0, 2, -1]))
        np.testing.assert_allclose(p[0], [1, 0, 0, 0, 0])
        self.assertEqual(p[1, 2], 0)
        np.testing.assert_allclose(p[2], dest[2])
        np.testing.assert_allclose(p.sum(axis=1), 1)

    def test_causal_age_resets_at_class_change_and_match(self):
        _, age = causal_age(self.features)
        np.testing.assert_array_equal(age[:7], [0, 1, 2, 0, 1, 2, 0])
        self.assertEqual(age[16], 0)

    def test_duration_censoring_and_probability_conservation(self):
        forecasts, fitted = fit_duration(self.features, self.train)
        # Three 16-frame training sequences: only 15 observed adjacent pairs each.
        self.assertEqual(fitted["exposure"].sum(), 45)
        for p in forecasts.values():
            self.assertTrue(np.isfinite(p).all())
            np.testing.assert_allclose(p.sum(1), 1)
        mutated = self.features.copy()
        mutated.loc[~self.train, "self_action_id"] = 100
        _, refit = fit_duration(mutated, self.train)
        np.testing.assert_allclose(refit["hazard"], fitted["hazard"])
        np.testing.assert_allclose(refit["jumps"], fitted["jumps"])

    def test_context_train_only_and_causal_features(self):
        predictions, fitted = fit_context_transitions(self.features, self.train, self.model, self.beliefs, 10)
        np.testing.assert_allclose(fitted["matrices"].sum(axis=1), 1)
        for p in predictions.values():
            np.testing.assert_allclose(p.sum(1), 1)
        changed = self.features.copy()
        # Mutate only future observations within one held-out match.
        match = changed.index[changed.match_id.eq("m3")].to_numpy()
        changed.loc[match[8:], "self_action_id"] = 100
        altered_beliefs = infer_frame_beliefs(changed, self.model)
        np.testing.assert_allclose(belief_features(self.model, self.beliefs, 3)[match[:8]],
                                   belief_features(self.model, altered_beliefs, 3)[match[:8]])
        revised, refit = fit_context_transitions(changed, self.train, self.model, altered_beliefs, 10)
        np.testing.assert_allclose(fitted["matrices"], refit["matrices"])
        np.testing.assert_allclose(predictions[3][match[:8]], revised[3][match[:8]])


if __name__ == "__main__":
    unittest.main()
