"""Autoregressive one-frame rollout evaluation for requested time horizons.

For a rollout starting at frame t, only the state at t is treated as real.  A
one-frame model predicts t+1, that predicted action is inserted into the state
for the next call, and the process repeats until t+h.  The endpoint and every
intermediate prediction are compared with the offline canonical log.

Only action-derived state can be rolled forward from these CSV logs.  Position,
velocity, health, opponent state, and event features are held at the starting
frame because reproducing their future values requires Unity physics rollback/
resimulation.  No future logged feature is supplied to either predictor.
"""

from __future__ import annotations

import argparse
import json
from collections import deque
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from xgboost import XGBClassifier

from .active_inference import (
    ActiveInferenceModel,
    active_inference_probabilities,
    infer_frame_beliefs,
    load_active_inference_model,
    tune_scoring_parameters,
)
from .constants import ACTION_CLASSES, action_id_to_class
from .data import load_match_logs
from .evaluation import evaluate_probabilities
from .features import FEATURE_COLUMNS, build_feature_frame, build_supervised_dataset
from .split import apply_session_split
from .train import _expand_probabilities, _new_model

ROLLOUT_HORIZONS: dict[str, tuple[int, int]] = {
    "50ms": (50, 3),
    "100ms": (100, 6),
    "200ms": (200, 12),
    "500ms": (500, 30),
    "1000ms": (1000, 60),
    "2000ms": (2000, 120),
    "3000ms": (3000, 180),
}

CANONICAL_ACTION_IDS = np.asarray([0, 1, 2, 100, 301], dtype=np.float32)


def _feature_index(name: str) -> int:
    return FEATURE_COLUMNS.index(name)


FEATURE_INDEX = {name: _feature_index(name) for name in FEATURE_COLUMNS}


def build_rollout_paths(
    feature_frame: pd.DataFrame,
    split_assignment: dict[str, str],
    horizon_frames: int,
) -> np.ndarray:
    """Return index paths [t, t+1, ..., t+h] for every valid test start."""
    paths: list[np.ndarray] = []
    is_test = feature_frame["session_id"].astype(str).map(split_assignment).eq("test")
    test = feature_frame[is_test]
    for _, group in test.sort_values(["match_id", "player_id", "frame"]).groupby(
        ["match_id", "player_id"], sort=False
    ):
        indices = group.index.to_numpy(dtype=int)
        frames = group["frame"].to_numpy(dtype=int)
        classes = group["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
        for position in range(0, len(group) - horizon_frames):
            endpoint = position + horizon_frames
            if frames[endpoint] - frames[position] != horizon_frames:
                continue
            if classes[position] < 0 or classes[endpoint] < 0:
                continue
            paths.append(indices[position : endpoint + 1])
    if not paths:
        return np.empty((0, horizon_frames + 1), dtype=int)
    return np.stack(paths)


def _train_one_frame_xgboost(
    features: pd.DataFrame,
    split_assignment: dict[str, str],
    model_path: Path,
    seed: int,
    n_estimators: int,
    max_depth: int,
) -> tuple[XGBClassifier, dict[str, Any]]:
    dataset = build_supervised_dataset(features, horizon_frames=1)
    dataset["split"] = apply_session_split(dataset, split_assignment)
    train = dataset[dataset["split"] == "train"]
    validation = dataset[dataset["split"] == "validation"]
    test = dataset[dataset["split"] == "test"]
    if train.empty or validation.empty or test.empty:
        raise ValueError("One-frame training requires non-empty train/validation/test splits")

    model = _new_model(seed, n_estimators, max_depth)
    model.fit(
        train[FEATURE_COLUMNS].to_numpy(dtype=np.float32),
        train["target_class"].to_numpy(dtype=int),
        eval_set=[
            (
                validation[FEATURE_COLUMNS].to_numpy(dtype=np.float32),
                validation["target_class"].to_numpy(dtype=int),
            )
        ],
        verbose=False,
    )
    model_path.parent.mkdir(parents=True, exist_ok=True)
    model.save_model(model_path)
    metadata = {
        "model_json": model_path.name,
        "prediction_frames": 1,
        "prediction_milliseconds_at_60fps": 1000 / 60,
        "best_iteration": int(model.best_iteration),
        "sample_counts": {
            "train": len(train),
            "validation": len(validation),
            "test": len(test),
        },
    }
    return model, metadata


def _tune_one_frame_fep(
    features: pd.DataFrame,
    split_assignment: dict[str, str],
    model: ActiveInferenceModel,
    beliefs: np.ndarray,
) -> tuple[dict[str, float], float]:
    dataset = build_supervised_dataset(features, horizon_frames=1)
    dataset["split"] = apply_session_split(dataset, split_assignment)
    validation = dataset[dataset["split"] == "validation"]
    return tune_scoring_parameters(
        model,
        beliefs[validation.index],
        validation["target_class"].to_numpy(dtype=int),
        horizon_frames=1,
    )


def _override_predicted_action_features(
    values: np.ndarray,
    action_class: np.ndarray,
    action_frame: np.ndarray,
    previous_one: np.ndarray,
    previous_two: np.ndarray,
    attack_count: np.ndarray,
    guard_count: np.ndarray,
) -> None:
    """Replace every feature derivable from the recursively predicted action."""
    values[:, FEATURE_INDEX["self_action_id"]] = CANONICAL_ACTION_IDS[action_class]
    values[:, FEATURE_INDEX["self_action_group"]] = action_class
    values[:, FEATURE_INDEX["self_action_frame"]] = action_frame
    values[:, FEATURE_INDEX["previous_action_id_1"]] = previous_one
    values[:, FEATURE_INDEX["previous_action_id_2"]] = previous_two
    values[:, FEATURE_INDEX["attack_frames_last_30"]] = attack_count
    values[:, FEATURE_INDEX["guard_frames_last_30"]] = guard_count

    facing_right = values[:, FEATURE_INDEX["self_is_face_right"]] > 0.5
    forward = action_class == 1
    backward = (action_class == 2) | (action_class == 4)
    left = (forward & ~facing_right) | (backward & facing_right)
    right = (forward & facing_right) | (backward & ~facing_right)
    attack = action_class == 3
    values[:, FEATURE_INDEX["input_left"]] = left
    values[:, FEATURE_INDEX["input_right"]] = right
    values[:, FEATURE_INDEX["input_attack"]] = attack
    values[:, FEATURE_INDEX["input_bits"]] = (
        left.astype(np.int8)
        | (right.astype(np.int8) << 1)
        | (attack.astype(np.int8) << 2)
    )
    # A class forecast has no reliable press/release edge information.
    values[:, FEATURE_INDEX["input_down_bits"]] = 0
    values[:, FEATURE_INDEX["input_up_bits"]] = 0


def _update_action_state(
    predicted: np.ndarray,
    current: np.ndarray,
    action_frame: np.ndarray,
    previous_one: np.ndarray,
    previous_two: np.ndarray,
    attack_count: np.ndarray,
    guard_count: np.ndarray,
    predicted_attacks: deque[np.ndarray],
    predicted_guards: deque[np.ndarray],
    outgoing_actual: np.ndarray | None,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    changed = predicted != current
    new_previous_two = np.where(changed, previous_one, previous_two)
    new_previous_one = np.where(changed, CANONICAL_ACTION_IDS[current], previous_one)
    new_action_frame = np.where(changed, 0, action_frame + 1)

    new_attack = (predicted == 3).astype(np.int16)
    new_guard = (predicted == 4).astype(np.int16)
    predicted_attacks.append(new_attack)
    predicted_guards.append(new_guard)
    attack_count += new_attack
    guard_count += new_guard
    if outgoing_actual is not None:
        attack_count -= (outgoing_actual == 3).astype(np.int16)
        guard_count -= (outgoing_actual == 4).astype(np.int16)
    elif len(predicted_attacks) > 30:
        attack_count -= predicted_attacks.popleft()
        guard_count -= predicted_guards.popleft()
    return predicted, new_action_frame, new_previous_one, new_previous_two


def _endpoint_record_frame(
    features: pd.DataFrame,
    paths: np.ndarray,
    model_name: str,
    horizon_name: str,
    milliseconds: int,
    predictions: np.ndarray,
    probabilities: np.ndarray,
    process_correct: np.ndarray,
    process_samples: np.ndarray,
    intermediate_correct: np.ndarray,
    intermediate_samples: np.ndarray,
    complete_path: np.ndarray,
) -> pd.DataFrame:
    start = features.loc[paths[:, 0]]
    endpoint = features.loc[paths[:, -1]]
    start_class = start["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
    target_class = endpoint["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
    result = pd.DataFrame(
        {
            "horizon": horizon_name,
            "milliseconds": milliseconds,
            "frames": paths.shape[1] - 1,
            "model": model_name,
            "match_id": start["match_id"].to_numpy(),
            "session_id": start["session_id"].to_numpy(),
            "player_id": start["player_id"].to_numpy(dtype=int),
            "source_frame": start["frame"].to_numpy(dtype=int),
            "target_frame": endpoint["frame"].to_numpy(dtype=int),
            "actual_start_class": start_class,
            "target_class": target_class,
            "predicted_class": predictions,
            "prediction_correct": predictions == target_class,
            "actual_action_changed": start_class != target_class,
            "process_correct_predictions": process_correct,
            "process_comparable_predictions": process_samples,
            "process_accuracy": process_correct / np.maximum(process_samples, 1),
            "intermediate_correct_predictions": intermediate_correct,
            "intermediate_comparable_predictions": intermediate_samples,
            "intermediate_accuracy": intermediate_correct / np.maximum(intermediate_samples, 1),
            "complete_path_correct": complete_path,
        }
    )
    for class_index, class_name in enumerate(ACTION_CLASSES):
        result[f"probability_{class_name.lower()}"] = probabilities[:, class_index]
    return result


def _run_xgboost_rollout(
    features: pd.DataFrame,
    paths: np.ndarray,
    model: XGBClassifier,
    horizon_name: str,
    milliseconds: int,
) -> tuple[pd.DataFrame, list[dict[str, Any]]]:
    base_values = features[FEATURE_COLUMNS].to_numpy(dtype=np.float32)
    actual_classes = features["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
    frame_numbers = features["frame"].to_numpy(dtype=int)
    match_ids = features["match_id"].astype(str).to_numpy()
    player_ids = features["player_id"].to_numpy(dtype=int)
    count = len(paths)
    start_indices = paths[:, 0]
    current = actual_classes[paths[:, 0]].copy()
    action_frame = base_values[paths[:, 0], FEATURE_INDEX["self_action_frame"]].copy()
    previous_one = base_values[paths[:, 0], FEATURE_INDEX["previous_action_id_1"]].copy()
    previous_two = base_values[paths[:, 0], FEATURE_INDEX["previous_action_id_2"]].copy()
    attack_count = base_values[
        start_indices, FEATURE_INDEX["attack_frames_last_30"]
    ].astype(np.int16)
    guard_count = base_values[
        start_indices, FEATURE_INDEX["guard_frames_last_30"]
    ].astype(np.int16)
    predicted_attacks: deque[np.ndarray] = deque()
    predicted_guards: deque[np.ndarray] = deque()
    process_correct = np.zeros(count, dtype=np.int32)
    process_samples = np.zeros(count, dtype=np.int32)
    intermediate_correct = np.zeros(count, dtype=np.int32)
    intermediate_samples = np.zeros(count, dtype=np.int32)
    complete_path = np.ones(count, dtype=bool)
    step_rows: list[dict[str, Any]] = []
    final_probabilities = np.empty((count, len(ACTION_CLASSES)), dtype=float)
    final_predictions = np.empty(count, dtype=int)
    horizon_frames = paths.shape[1] - 1

    for step in range(1, horizon_frames + 1):
        # A pure forecast must not read any feature from t+1 onward.  Physical
        # features stay at t; only predicted action-derived state is advanced.
        values = base_values[start_indices].copy()
        if step > 1:
            _override_predicted_action_features(
                values,
                current,
                action_frame,
                previous_one,
                previous_two,
                attack_count,
                guard_count,
            )
        probabilities = _expand_probabilities(model, values)
        predicted = probabilities.argmax(axis=1)
        actual = actual_classes[paths[:, step]]
        valid = actual >= 0
        correct = predicted == actual
        process_correct += correct & valid
        process_samples += valid
        complete_path &= ~valid | correct
        if step < horizon_frames:
            intermediate_correct += correct & valid
            intermediate_samples += valid
        step_rows.append(
            {
                "horizon": horizon_name,
                "milliseconds": milliseconds,
                "frames": horizon_frames,
                "model": "xgboost",
                "step": step,
                "step_time_ms_at_60fps": step * 1000 / 60,
                "samples": int(valid.sum()),
                "correct": int((correct & valid).sum()),
                "accuracy": float(correct[valid].mean()) if valid.any() else None,
            }
        )
        if step == horizon_frames:
            final_probabilities = probabilities
            final_predictions = predicted
        outgoing_actual: np.ndarray | None = None
        if step <= 30:
            candidate = start_indices - 30 + step
            valid_candidate = candidate >= 0
            safe_candidate = np.maximum(candidate, 0)
            valid_candidate &= match_ids[safe_candidate] == match_ids[start_indices]
            valid_candidate &= player_ids[safe_candidate] == player_ids[start_indices]
            valid_candidate &= (
                frame_numbers[safe_candidate]
                == frame_numbers[start_indices] - 30 + step
            )
            outgoing_actual = np.where(
                valid_candidate, actual_classes[safe_candidate], -1
            )
        current, action_frame, previous_one, previous_two = _update_action_state(
            predicted,
            current,
            action_frame,
            previous_one,
            previous_two,
            attack_count,
            guard_count,
            predicted_attacks,
            predicted_guards,
            outgoing_actual,
        )

    return (
        _endpoint_record_frame(
            features,
            paths,
            "xgboost",
            horizon_name,
            milliseconds,
            final_predictions,
            final_probabilities,
            process_correct,
            process_samples,
            intermediate_correct,
            intermediate_samples,
            complete_path,
        ),
        step_rows,
    )


def _observe_predicted_actions(
    model: ActiveInferenceModel,
    beliefs: np.ndarray,
    predicted: np.ndarray,
) -> np.ndarray:
    projected = beliefs @ model.transition.T
    projected *= model.observation_likelihood[predicted]
    projected /= np.clip(projected.sum(axis=1, keepdims=True), 1e-12, None)
    return projected


def _run_fep_rollout(
    features: pd.DataFrame,
    paths: np.ndarray,
    model: ActiveInferenceModel,
    initial_beliefs: np.ndarray,
    scoring_parameters: dict[str, float],
    horizon_name: str,
    milliseconds: int,
) -> tuple[pd.DataFrame, list[dict[str, Any]]]:
    actual_classes = features["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
    count = len(paths)
    beliefs = initial_beliefs[paths[:, 0]].copy()
    process_correct = np.zeros(count, dtype=np.int32)
    process_samples = np.zeros(count, dtype=np.int32)
    intermediate_correct = np.zeros(count, dtype=np.int32)
    intermediate_samples = np.zeros(count, dtype=np.int32)
    complete_path = np.ones(count, dtype=bool)
    step_rows: list[dict[str, Any]] = []
    final_probabilities = np.empty((count, len(ACTION_CLASSES)), dtype=float)
    final_predictions = np.empty(count, dtype=int)
    horizon_frames = paths.shape[1] - 1

    for step in range(1, horizon_frames + 1):
        probabilities = active_inference_probabilities(
            model,
            beliefs,
            horizon_frames=1,
            **scoring_parameters,
        )
        predicted = probabilities.argmax(axis=1)
        actual = actual_classes[paths[:, step]]
        valid = actual >= 0
        correct = predicted == actual
        process_correct += correct & valid
        process_samples += valid
        complete_path &= ~valid | correct
        if step < horizon_frames:
            intermediate_correct += correct & valid
            intermediate_samples += valid
        step_rows.append(
            {
                "horizon": horizon_name,
                "milliseconds": milliseconds,
                "frames": horizon_frames,
                "model": "fep",
                "step": step,
                "step_time_ms_at_60fps": step * 1000 / 60,
                "samples": int(valid.sum()),
                "correct": int((correct & valid).sum()),
                "accuracy": float(correct[valid].mean()) if valid.any() else None,
            }
        )
        if step == horizon_frames:
            final_probabilities = probabilities
            final_predictions = predicted
        beliefs = _observe_predicted_actions(model, beliefs, predicted)

    return (
        _endpoint_record_frame(
            features,
            paths,
            "fep",
            horizon_name,
            milliseconds,
            final_predictions,
            final_probabilities,
            process_correct,
            process_samples,
            intermediate_correct,
            intermediate_samples,
            complete_path,
        ),
        step_rows,
    )


def _summarize(endpoints: pd.DataFrame) -> pd.DataFrame:
    probability_columns = [f"probability_{name.lower()}" for name in ACTION_CLASSES]
    rows: list[dict[str, Any]] = []
    for (horizon, model), group in endpoints.groupby(["horizon", "model"], sort=False):
        metrics = evaluate_probabilities(
            group["target_class"].to_numpy(dtype=int),
            group[probability_columns].to_numpy(dtype=float),
            group["actual_start_class"].to_numpy(dtype=int),
        )
        intermediate_samples = int(group["intermediate_comparable_predictions"].sum())
        rows.append(
            {
                "horizon": horizon,
                "milliseconds": int(group["milliseconds"].iloc[0]),
                "frames": int(group["frames"].iloc[0]),
                "model": model,
                "rollouts": len(group),
                "endpoint_accuracy": metrics["accuracy"],
                "endpoint_top2_accuracy": metrics["top2_accuracy"],
                "endpoint_macro_f1": metrics["macro_f1"],
                "endpoint_log_loss": metrics["log_loss"],
                "process_accuracy": float(
                    group["process_correct_predictions"].sum()
                    / group["process_comparable_predictions"].sum()
                ),
                "intermediate_accuracy": (
                    float(
                        group["intermediate_correct_predictions"].sum()
                        / intermediate_samples
                    )
                    if intermediate_samples
                    else None
                ),
                "mean_rollout_process_accuracy": float(group["process_accuracy"].mean()),
                "complete_path_accuracy": float(group["complete_path_correct"].mean()),
                **{
                    key: value
                    for key, value in metrics.items()
                    if key
                    in {
                        "changed_action_samples",
                        "changed_action_accuracy",
                        "unchanged_action_samples",
                        "unchanged_action_accuracy",
                    }
                },
            }
        )
    result = pd.DataFrame(rows)
    order = {name: index for index, name in enumerate(ROLLOUT_HORIZONS)}
    result["_order"] = result["horizon"].map(order)
    return result.sort_values(["_order", "model"]).drop(columns="_order").reset_index(drop=True)


def _format_accuracy(value: object) -> str:
    return "N/A" if value is None or pd.isna(value) else f"{float(value):.3f}"


def _write_report(summary: pd.DataFrame, metadata: dict[str, Any], path: Path) -> None:
    indexed = summary.set_index(["horizon", "model"])
    lines = [
        "# 1フレーム逐次予測による自己回帰ホライズン実験",
        "",
        "各開始フレームの実状態から1フレーム先を予測し、その予測行動を正しい現在行動と仮定して次の1フレームを予測する処理を、指定ホライズンまで繰り返しました。例えば50msは `t→t+1→t+2→t+3` の3回予測であり、最終的なt+3と実際の行動を比較します。開始フレームを1フレームずつずらし、正解終端が存在する全test系列で実行しています。",
        "",
        f"- 1フレームXGBoost: train {metadata['xgboost']['sample_counts']['train']:,} / validation {metadata['xgboost']['sample_counts']['validation']:,} / test {metadata['xgboost']['sample_counts']['test']:,}",
        f"- FEP 1フレームvalidation log loss: {metadata['fep']['validation_log_loss']:.3f}",
        "- 変化あり/なし: 開始フレームの実行動と終端フレームの実行動が異なるかで判定",
        "",
        "## 最終到達点と途中過程の一致率",
        "",
        "途中Accuracyは終端より前の全予測、過程Accuracyは途中と終端を含む全1フレーム予測、全経路一致率は比較可能な全ステップが正解だったrolloutの割合です。",
        "",
        "|予測時間|rollout数|XGB終端|FEP終端|XGB途中|FEP途中|XGB過程|FEP過程|XGB全経路|FEP全経路|",
        "|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for horizon in ROLLOUT_HORIZONS:
        xgb = indexed.loc[(horizon, "xgboost")]
        fep = indexed.loc[(horizon, "fep")]
        lines.append(
            f"|{horizon}|{int(xgb['rollouts']):,}|{xgb['endpoint_accuracy']:.3f}|"
            f"{fep['endpoint_accuracy']:.3f}|{_format_accuracy(xgb['intermediate_accuracy'])}|"
            f"{_format_accuracy(fep['intermediate_accuracy'])}|{xgb['process_accuracy']:.3f}|"
            f"{fep['process_accuracy']:.3f}|{xgb['complete_path_accuracy']:.3f}|"
            f"{fep['complete_path_accuracy']:.3f}|"
        )
    lines.extend(
        [
            "",
            "## 終端での実行動変化の有無",
            "",
            "|予測時間|変化あり件数|XGB変化あり|FEP変化あり|変化なし件数|XGB変化なし|FEP変化なし|",
            "|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for horizon in ROLLOUT_HORIZONS:
        xgb = indexed.loc[(horizon, "xgboost")]
        fep = indexed.loc[(horizon, "fep")]
        lines.append(
            f"|{horizon}|{int(xgb['changed_action_samples']):,}|"
            f"{_format_accuracy(xgb['changed_action_accuracy'])}|"
            f"{_format_accuracy(fep['changed_action_accuracy'])}|"
            f"{int(xgb['unchanged_action_samples']):,}|"
            f"{_format_accuracy(xgb['unchanged_action_accuracy'])}|"
            f"{_format_accuracy(fep['unchanged_action_accuracy'])}|"
        )
    xgb_50 = indexed.loc[("50ms", "xgboost")]
    fep_50 = indexed.loc[("50ms", "fep")]
    xgb_500 = indexed.loc[("500ms", "xgboost")]
    fep_500 = indexed.loc[("500ms", "fep")]
    xgb_3000 = indexed.loc[("3000ms", "xgboost")]
    fep_3000 = indexed.loc[("3000ms", "fep")]
    lines.extend(
        [
            "",
            "## 所見",
            "",
            (
                f"50ms（3回の逐次予測）では、XGBoostの終端Accuracyは{xgb_50['endpoint_accuracy']:.3f}、"
                f"途中Accuracyは{xgb_50['intermediate_accuracy']:.3f}、全3ステップ一致率は"
                f"{xgb_50['complete_path_accuracy']:.3f}でした。FEPはそれぞれ"
                f"{fep_50['endpoint_accuracy']:.3f}、{fep_50['intermediate_accuracy']:.3f}、"
                f"{fep_50['complete_path_accuracy']:.3f}でした。"
            ),
            "",
            (
                f"500msでは全経路一致率がXGBoost {xgb_500['complete_path_accuracy']:.3f}、"
                f"FEP {fep_500['complete_path_accuracy']:.3f}まで低下しました。3000msでは両方式とも"
                f"全経路一致率が{xgb_3000['complete_path_accuracy']:.3f}/{fep_3000['complete_path_accuracy']:.3f}で、"
                "途中のどこかで必ず実行動との差が生じています。終端Accuracyだけでは、この途中誤差を捉えられません。"
            ),
            "",
            "予測時間が長いほど終端まで残る長い対戦系列だけが対象になるため、異なる予測時間のAccuracyは完全に同一母集団の比較ではありません。特に2000ms・3000msの終端Accuracyが横ばいまたは上昇して見える箇所は、誤差回復だけでなく対象系列とクラス構成の変化を含みます。",
            "",
            "## 評価上の制約",
            "",
            "未来ログの特徴量はモデル入力に使用していません。開始フレームから再帰的に更新するのは、自分の予測行動クラス、入力方向・攻撃ビット、行動継続フレーム、直前の異なる行動、および直近30フレームのAttack/Guard回数です。位置・速度・体力・相手状態・ヒットイベントは開始フレームの値で固定しています。これらの物理状態まで予測行動に応じて進める完全な自己回帰評価には、同じ開始フレーム群をUnityのrollback/resimulationで再実行する必要があります。",
            "",
        ]
    )
    path.write_text("\n".join(lines), encoding="utf-8")


def evaluate_autoregressive_horizons(
    model_dir: str | Path,
    logs_path: str | Path,
    output_dir: str | Path,
    seed: int = 42,
    n_estimators: int = 500,
    max_depth: int = 6,
) -> dict[str, Any]:
    artifact_root = Path(model_dir)
    output = Path(output_dir)
    models_dir = output / "models"
    output.mkdir(parents=True, exist_ok=True)
    models_dir.mkdir(parents=True, exist_ok=True)
    split_assignment = json.loads(
        (artifact_root / "split_assignment.json").read_text(encoding="utf-8")
    )
    logs = load_match_logs(logs_path)
    features = build_feature_frame(logs.frames, logs.events)

    xgboost, xgboost_metadata = _train_one_frame_xgboost(
        features,
        split_assignment,
        models_dir / "model_1frame.json",
        seed,
        n_estimators,
        max_depth,
    )
    fep = load_active_inference_model(
        artifact_root / "models" / "model_active_inference.npz"
    )
    beliefs = infer_frame_beliefs(features, fep)
    fep_parameters, fep_validation_loss = _tune_one_frame_fep(
        features, split_assignment, fep, beliefs
    )

    endpoint_frames: list[pd.DataFrame] = []
    per_step_rows: list[dict[str, Any]] = []
    rollout_counts: dict[str, int] = {}
    for horizon_name, (milliseconds, horizon_frames) in ROLLOUT_HORIZONS.items():
        paths = build_rollout_paths(features, split_assignment, horizon_frames)
        if len(paths) == 0:
            raise ValueError(f"No valid rollout paths for {horizon_name}")
        rollout_counts[horizon_name] = len(paths)
        xgb_endpoints, xgb_steps = _run_xgboost_rollout(
            features, paths, xgboost, horizon_name, milliseconds
        )
        fep_endpoints, fep_steps = _run_fep_rollout(
            features,
            paths,
            fep,
            beliefs,
            fep_parameters,
            horizon_name,
            milliseconds,
        )
        endpoint_frames.extend([xgb_endpoints, fep_endpoints])
        per_step_rows.extend(xgb_steps)
        per_step_rows.extend(fep_steps)
        print(
            f"{horizon_name:>6}: rollouts={len(paths):5d} "
            f"XGB={xgb_endpoints['prediction_correct'].mean():.4f} "
            f"FEP={fep_endpoints['prediction_correct'].mean():.4f}",
            flush=True,
        )

    endpoints = pd.concat(endpoint_frames, ignore_index=True)
    per_step = pd.DataFrame(per_step_rows)
    summary = _summarize(endpoints)
    metadata = {
        "experiment": "one-frame autoregressive action rollout",
        "random_seed": seed,
        "source_logs": str(Path(logs_path).resolve()),
        "feature_state_policy": (
            "predicted action-derived features; physical/opponent/event features held at rollout start"
        ),
        "horizons": {
            name: {"milliseconds": milliseconds, "frames": frames, "rollouts": rollout_counts[name]}
            for name, (milliseconds, frames) in ROLLOUT_HORIZONS.items()
        },
        "xgboost": xgboost_metadata,
        "fep": {
            "prediction_frames": 1,
            "scoring_parameters": fep_parameters,
            "validation_log_loss": fep_validation_loss,
        },
    }
    endpoints.to_csv(output / "autoregressive_endpoints.csv", index=False)
    per_step.to_csv(output / "autoregressive_per_step.csv", index=False)
    summary.to_csv(output / "autoregressive_summary.csv", index=False)
    (output / "metadata.json").write_text(
        json.dumps(metadata, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    _write_report(summary, metadata, output / "autoregressive_report.md")
    return {
        "endpoints": endpoints,
        "per_step": per_step,
        "summary": summary,
        "metadata": metadata,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--logs", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--n-estimators", type=int, default=500)
    parser.add_argument("--max-depth", type=int, default=6)
    arguments = parser.parse_args()
    result = evaluate_autoregressive_horizons(
        arguments.model_dir,
        arguments.logs,
        arguments.output,
        seed=arguments.seed,
        n_estimators=arguments.n_estimators,
        max_depth=arguments.max_depth,
    )
    print(result["summary"].to_string(index=False))


if __name__ == "__main__":
    main()
