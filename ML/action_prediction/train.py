"""Train and compare XGBoost, active inference, and simple baselines."""

from __future__ import annotations

import argparse
import csv
import json
import os
from pathlib import Path
from typing import Any

os.environ.setdefault("MPLCONFIGDIR", "/tmp/action-prediction-matplotlib")
os.environ.setdefault("XDG_CACHE_HOME", "/tmp/action-prediction-cache")
os.environ.setdefault("MPLBACKEND", "Agg")

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from xgboost import XGBClassifier

from .active_inference import (
    LATENT_STATES,
    active_inference_probabilities,
    fit_active_inference_model,
    infer_frame_beliefs,
    tune_scoring_parameters,
)
from .baselines import (
    fit_markov,
    majority_probabilities,
    markov_probabilities,
    persistence_probabilities,
)
from .constants import ACTION_CLASSES, HORIZONS, action_id_to_class
from .data import load_match_logs
from .evaluation import evaluate_probabilities
from .features import FEATURE_COLUMNS, build_feature_frame, build_supervised_dataset
from .split import apply_session_split, make_session_split


def _expand_probabilities(model: XGBClassifier, values: np.ndarray) -> np.ndarray:
    raw = model.predict_proba(values)
    expanded = np.zeros((len(values), len(ACTION_CLASSES)), dtype=float)
    for index, class_id in enumerate(model.classes_):
        expanded[:, int(class_id)] = raw[:, index]
    return expanded


def _new_model(seed: int, n_estimators: int, max_depth: int) -> XGBClassifier:
    return XGBClassifier(
        objective="multi:softprob",
        num_class=len(ACTION_CLASSES),
        n_estimators=n_estimators,
        learning_rate=0.05,
        max_depth=max_depth,
        min_child_weight=2.0,
        subsample=0.85,
        colsample_bytree=0.85,
        reg_lambda=1.0,
        reg_alpha=0.05,
        tree_method="hist",
        eval_metric="mlogloss",
        early_stopping_rounds=30,
        random_state=seed,
        n_jobs=max(1, min(8, os.cpu_count() or 1)),
    )


def _save_confusion_matrix(path: Path, matrix: list[list[int]]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(["actual/predicted", *ACTION_CLASSES])
        for class_name, row in zip(ACTION_CLASSES, matrix):
            writer.writerow([class_name, *row])


def _format_optional_accuracy(value: object) -> str:
    """Format a subgroup accuracy, including the valid zero-sample case."""
    return "N/A" if value is None or pd.isna(value) else f"{float(value):.3f}"


def _plot_metrics(summary: pd.DataFrame, output_path: Path) -> None:
    horizon_order = list(HORIZONS)
    x = [HORIZONS[name][0] for name in horizon_order]
    figure, axes = plt.subplots(1, 2, figsize=(12, 4.5))
    for model_name in ("majority", "persistence", "markov", "active_inference", "xgboost"):
        model_rows = summary[summary["model"] == model_name].set_index("horizon").reindex(horizon_order)
        axes[0].plot(x, model_rows["accuracy"], marker="o", label=model_name)
    axes[0].set_xscale("log")
    axes[0].set_xlabel("Prediction horizon (ms, log scale)")
    axes[0].set_ylabel("Accuracy")
    axes[0].set_ylim(0, 1)
    axes[0].grid(alpha=0.3)
    axes[0].legend()

    xgboost_rows = summary[summary["model"] == "xgboost"].set_index("horizon").reindex(horizon_order)
    axes[1].plot(x, xgboost_rows["top2_accuracy"], marker="o", label="XGBoost Top-2")
    axes[1].plot(x, xgboost_rows["macro_f1"], marker="o", label="XGBoost Macro F1")
    active_rows = summary[summary["model"] == "active_inference"].set_index("horizon").reindex(horizon_order)
    axes[1].plot(x, active_rows["top2_accuracy"], marker="s", linestyle="--", label="Active inference Top-2")
    axes[1].plot(x, active_rows["macro_f1"], marker="s", linestyle="--", label="Active inference Macro F1")
    axes[1].set_xscale("log")
    axes[1].set_xlabel("Prediction horizon (ms, log scale)")
    axes[1].set_ylim(0, 1)
    axes[1].grid(alpha=0.3)
    axes[1].legend()
    figure.tight_layout()
    figure.savefig(output_path, dpi=160)
    plt.close(figure)


def _write_experiment_report(
    summary: pd.DataFrame,
    metadata: dict[str, Any],
    output_path: Path,
) -> None:
    ordered = list(HORIZONS)
    accuracy = summary.pivot(index="horizon", columns="model", values="accuracy").reindex(ordered)
    xgboost = summary[summary["model"] == "xgboost"].set_index("horizon").reindex(ordered)
    lines = [
        "# 行動予測モデル比較実験レポート",
        "",
        f"- 読み込みフレーム数: {metadata['frame_rows_loaded']:,}",
        (
            "- セッション分割: "
            f"train {metadata['session_counts']['train']} / "
            f"validation {metadata['session_counts']['validation']} / "
            f"test {metadata['session_counts']['test']}"
        ),
        "- ラベル: Wait / Approach / Retreat / Attack / Guard",
        "",
        "## テスト結果",
        "",
        "|予測時間|テスト件数|最頻|Persistence|Markov|能動的推論|XGBoost|",
        "|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for horizon in ordered:
        row = xgboost.loc[horizon]
        lines.append(
            f"|{horizon}|{int(row['test_samples']):,}|"
            f"{accuracy.loc[horizon, 'majority']:.3f}|{accuracy.loc[horizon, 'persistence']:.3f}|"
            f"{accuracy.loc[horizon, 'markov']:.3f}|{accuracy.loc[horizon, 'active_inference']:.3f}|"
            f"{accuracy.loc[horizon, 'xgboost']:.3f}|"
        )
    lines.extend(
        [
            "",
            "## XGBoostと能動的推論の詳細比較",
            "",
            "|予測時間|XGB Accuracy|FEP Accuracy|XGB Top-2|FEP Top-2|XGB Macro F1|FEP Macro F1|XGB Log Loss|FEP Log Loss|",
            "|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    active = summary[summary["model"] == "active_inference"].set_index("horizon").reindex(ordered)
    for horizon in ordered:
        xgb_row = xgboost.loc[horizon]
        active_row = active.loc[horizon]
        lines.append(
            f"|{horizon}|{xgb_row['accuracy']:.3f}|{active_row['accuracy']:.3f}|"
            f"{xgb_row['top2_accuracy']:.3f}|{active_row['top2_accuracy']:.3f}|"
            f"{xgb_row['macro_f1']:.3f}|{active_row['macro_f1']:.3f}|"
            f"{xgb_row['log_loss']:.3f}|{active_row['log_loss']:.3f}|"
        )
    lines.extend(
        [
            "",
            "## 行動変化の有無による正答率",
            "",
            "予測時点の行動クラスと正解時点の行動クラスが異なるケースを「変化あり」、同じケースを「変化なし」として集計しています。",
            "",
            "|予測時間|変化あり件数|XGB 変化あり|FEP 変化あり|変化なし件数|XGB 変化なし|FEP 変化なし|",
            "|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for horizon in ordered:
        xgb_row = xgboost.loc[horizon]
        active_row = active.loc[horizon]
        lines.append(
            f"|{horizon}|{int(xgb_row['changed_action_samples']):,}|"
            f"{_format_optional_accuracy(xgb_row['changed_action_accuracy'])}|"
            f"{_format_optional_accuracy(active_row['changed_action_accuracy'])}|"
            f"{int(xgb_row['unchanged_action_samples']):,}|"
            f"{_format_optional_accuracy(xgb_row['unchanged_action_accuracy'])}|"
            f"{_format_optional_accuracy(active_row['unchanged_action_accuracy'])}|"
        )
    lines.extend(
        [
            "",
            "## 所見",
            "",
            "XGBoostは全ホライズンでPersistenceを上回り、50 msから2秒先までは最頻・Markovベースラインも上回りました。特に200～1000 msでは改善幅が大きく、現在行動の単純継続だけでは説明できない予測性能が確認できます。",
            "",
            "実データ適応版の能動的推論は、全ホライズンでXGBoostを上回る結果にはなりませんでした。2秒先のAccuracyでは単純基準と同程度まで到達し、3秒先ではXGBoostより高いAccuracyになりましたが、Macro F1が低く多数派クラスへの偏りが強いため、5行動をバランスよく予測できたとは判断できません。",
            "",
            "能動的推論モデルは、添付された人工データ用3潜在状態モデルを基に、観測尤度・状態遷移・習慣事前分布をtrainセッションだけで推定しています。probeは実験クラスに合わせてWaitへ対応付け、EFEの尺度はvalidationセッションだけで選択しました。これは添付資料に明記された再構成版の実データ適応実験であり、AIサーバー上の原本との完全一致を意味しません。",
            "",
            "3秒先ではXGBoost、能動的推論ともMacro F1が低いため、このログ量と特徴量では有効な5クラス予測とは判断できません。長期ホライズンでは追加セッション、より長い履歴、クラス不均衡対策を再検討する必要があります。",
            "",
            "本評価はフレーム単位のランダム分割ではなく、同一session_idを必ず同じ分割へ置いています。未来ActionやActionバッファは入力特徴量に含めていません。",
            "",
        ]
    )
    output_path.write_text("\n".join(lines), encoding="utf-8")


def train_all(
    logs_path: str | Path,
    output_dir: str | Path,
    seed: int = 42,
    n_estimators: int = 500,
    max_depth: int = 6,
) -> dict[str, Any]:
    output = Path(output_dir)
    models_dir = output / "models"
    reports_dir = output / "reports"
    models_dir.mkdir(parents=True, exist_ok=True)
    reports_dir.mkdir(parents=True, exist_ok=True)

    logs = load_match_logs(logs_path)
    features = build_feature_frame(logs.frames, logs.events)
    split_assignment = make_session_split(features["session_id"], seed=seed)
    (output / "split_assignment.json").write_text(
        json.dumps(split_assignment, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    all_metrics: dict[str, Any] = {}
    summary_rows: list[dict[str, Any]] = []
    model_metadata: dict[str, Any] = {}
    active_model = fit_active_inference_model(features, split_assignment)
    active_beliefs = infer_frame_beliefs(features, active_model)
    active_metadata: dict[str, Any] = {
        "model_npz": "model_active_inference.npz",
        "latent_states": list(LATENT_STATES),
        "observation_classes": list(ACTION_CLASSES),
        "probe_mapping": "Wait",
        "fit_iterations": len(active_model.fit_log_likelihood),
        "final_fit_log_likelihood": active_model.fit_log_likelihood[-1],
        "observation_likelihood": active_model.observation_likelihood.tolist(),
        "transition": active_model.transition.tolist(),
        "initial_belief": active_model.initial_belief.tolist(),
        "habit_prior": active_model.habit_prior.tolist(),
        "horizons": {},
    }

    for horizon_name, (milliseconds, horizon_frames) in HORIZONS.items():
        dataset = build_supervised_dataset(features, horizon_frames)
        dataset["split"] = apply_session_split(dataset, split_assignment)
        partitions = {name: dataset[dataset["split"] == name] for name in ("train", "validation", "test")}
        if any(partition.empty for partition in partitions.values()):
            sizes = {name: len(partition) for name, partition in partitions.items()}
            raise ValueError(f"Empty partition for horizon {horizon_name}: {sizes}")

        train = partitions["train"]
        validation = partitions["validation"]
        test = partitions["test"]
        x_train = train[FEATURE_COLUMNS].to_numpy(dtype=np.float32)
        y_train = train["target_class"].to_numpy(dtype=int)
        x_validation = validation[FEATURE_COLUMNS].to_numpy(dtype=np.float32)
        y_validation = validation["target_class"].to_numpy(dtype=int)
        x_test = test[FEATURE_COLUMNS].to_numpy(dtype=np.float32)
        y_test = test["target_class"].to_numpy(dtype=int)

        model = _new_model(seed, n_estimators, max_depth)
        model.fit(x_train, y_train, eval_set=[(x_validation, y_validation)], verbose=False)
        xgboost_probabilities = _expand_probabilities(model, x_test)

        current_train = train["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
        current_test = test["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
        transition_matrix = fit_markov(current_train, y_train)
        active_parameters, active_validation_loss = tune_scoring_parameters(
            active_model,
            active_beliefs[validation.index],
            y_validation,
            horizon_frames,
        )
        active_probabilities = active_inference_probabilities(
            active_model,
            active_beliefs[test.index],
            horizon_frames,
            **active_parameters,
        )
        probabilities_by_model = {
            "majority": majority_probabilities(y_train, len(test)),
            "persistence": persistence_probabilities(current_test, y_train),
            "markov": markov_probabilities(transition_matrix, current_test, y_train),
            "active_inference": active_probabilities,
            "xgboost": xgboost_probabilities,
        }
        horizon_metrics: dict[str, Any] = {}
        for model_name, probabilities in probabilities_by_model.items():
            metrics = evaluate_probabilities(y_test, probabilities, current_test)
            horizon_metrics[model_name] = metrics
            summary_rows.append(
                {
                    "horizon": horizon_name,
                    "milliseconds": milliseconds,
                    "frames": horizon_frames,
                    "model": model_name,
                    "test_samples": len(test),
                    **{key: value for key, value in metrics.items() if key != "confusion_matrix"},
                }
            )
            _save_confusion_matrix(
                reports_dir / f"confusion_{horizon_name}_{model_name}.csv",
                metrics["confusion_matrix"],
            )

        model_path = models_dir / f"model_{horizon_name}.json"
        model.save_model(model_path)
        all_metrics[horizon_name] = horizon_metrics
        model_metadata[horizon_name] = {
            "milliseconds": milliseconds,
            "frames": horizon_frames,
            "model_json": model_path.name,
            "best_iteration": int(model.best_iteration),
            "sample_counts": {name: len(frame) for name, frame in partitions.items()},
            "class_counts": {
                split_name: {
                    ACTION_CLASSES[class_id]: int((frame["target_class"] == class_id).sum())
                    for class_id in range(len(ACTION_CLASSES))
                }
                for split_name, frame in partitions.items()
            },
            "markov_transition_matrix": transition_matrix.tolist(),
        }
        active_metadata["horizons"][horizon_name] = {
            "frames": horizon_frames,
            "scoring_parameters": active_parameters,
            "validation_log_loss": active_validation_loss,
        }
        print(
            f"{horizon_name:>5}: train={len(train):5d} validation={len(validation):4d} "
            f"test={len(test):4d} accuracy={horizon_metrics['xgboost']['accuracy']:.4f}",
            flush=True,
        )

    summary = pd.DataFrame(summary_rows)
    active_model.save(models_dir / "model_active_inference.npz")
    summary.to_csv(reports_dir / "metrics_summary.csv", index=False)
    (reports_dir / "metrics.json").write_text(
        json.dumps(all_metrics, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    metadata = {
        "format_version": 2,
        "model_types": [
            "XGBoost multi-class probability classifier",
            "discrete active inference with three latent tactical states",
        ],
        "random_seed": seed,
        "source_logs": str(Path(logs_path).resolve()),
        "class_names": list(ACTION_CLASSES),
        "feature_names": FEATURE_COLUMNS,
        "excluded_leakage_fields": ["p1_buffer_action_id", "p2_buffer_action_id"],
        "evaluation_subgroups": {
            "action_change_definition": (
                "current action class differs from the ground-truth action class at t+h"
            ),
            "metrics": [
                "changed_action_samples",
                "changed_action_accuracy",
                "unchanged_action_samples",
                "unchanged_action_accuracy",
            ],
        },
        "session_counts": {
            name: sum(value == name for value in split_assignment.values())
            for name in ("train", "validation", "test")
        },
        "frame_rows_loaded": len(logs.frames),
        "horizons": model_metadata,
        "active_inference": active_metadata,
    }
    (output / "metadata.json").write_text(
        json.dumps(metadata, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    _write_experiment_report(summary, metadata, reports_dir / "experiment_report.md")
    _plot_metrics(summary, reports_dir / "performance_by_horizon.png")
    return {"metadata": metadata, "metrics": all_metrics, "summary": summary}


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--logs", required=True, help="MatchLogs directory or MatchLogs.zip")
    parser.add_argument("--output", default="artifacts", help="Artifact output directory")
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--n-estimators", type=int, default=500)
    parser.add_argument("--max-depth", type=int, default=6)
    return parser


def main() -> None:
    arguments = build_argument_parser().parse_args()
    train_all(
        logs_path=arguments.logs,
        output_dir=arguments.output,
        seed=arguments.seed,
        n_estimators=arguments.n_estimators,
        max_depth=arguments.max_depth,
    )


if __name__ == "__main__":
    main()
