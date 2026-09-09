"""Run a trained horizon model against MatchLogger CSV data."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd
from xgboost import XGBClassifier

from .active_inference import (
    active_inference_probabilities,
    infer_frame_beliefs,
    load_active_inference_model,
)
from .constants import ACTION_CLASSES, HORIZONS
from .data import load_match_logs
from .features import build_feature_frame


def predict_logs(
    model_dir: str | Path,
    logs_path: str | Path,
    horizon: str,
    output_path: str | Path,
    model_type: str = "xgboost",
) -> pd.DataFrame:
    if horizon not in HORIZONS:
        raise ValueError(f"Unknown horizon {horizon!r}; choose one of {list(HORIZONS)}")
    artifact_root = Path(model_dir)
    metadata = json.loads((artifact_root / "metadata.json").read_text(encoding="utf-8"))
    feature_names = metadata["feature_names"]
    logs = load_match_logs(logs_path)
    feature_frame = build_feature_frame(logs.frames, logs.events)
    if model_type == "xgboost":
        model = XGBClassifier()
        model.load_model(artifact_root / "models" / f"model_{horizon}.json")
        values = feature_frame[feature_names].to_numpy(dtype=np.float32)
        raw = model.predict_proba(values)
        probabilities = np.zeros((len(values), len(ACTION_CLASSES)), dtype=float)
        for index, class_id in enumerate(model.classes_):
            probabilities[:, int(class_id)] = raw[:, index]
    elif model_type == "active-inference":
        model = load_active_inference_model(artifact_root / "models" / "model_active_inference.npz")
        beliefs = infer_frame_beliefs(feature_frame, model)
        horizon_metadata = metadata["active_inference"]["horizons"][horizon]
        probabilities = active_inference_probabilities(
            model,
            beliefs,
            HORIZONS[horizon][1],
            **horizon_metadata["scoring_parameters"],
        )
    else:
        raise ValueError("model_type must be 'xgboost' or 'active-inference'")
    output = feature_frame[["match_id", "session_id", "player_id", "frame"]].copy()
    for index, class_name in enumerate(ACTION_CLASSES):
        output[f"probability_{class_name.lower()}"] = probabilities[:, index]
    output["predicted_class"] = probabilities.argmax(axis=1)
    output["predicted_action"] = output["predicted_class"].map(dict(enumerate(ACTION_CLASSES)))
    destination = Path(output_path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    output.to_csv(destination, index=False)
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", required=True, help="Artifact directory produced by training")
    parser.add_argument("--logs", required=True, help="MatchLogs directory or MatchLogs.zip")
    parser.add_argument("--horizon", required=True, choices=list(HORIZONS))
    parser.add_argument("--model-type", choices=("xgboost", "active-inference"), default="xgboost")
    parser.add_argument("--output", required=True, help="Output predictions.csv")
    arguments = parser.parse_args()
    output = predict_logs(
        arguments.model_dir,
        arguments.logs,
        arguments.horizon,
        arguments.output,
        model_type=arguments.model_type,
    )
    print(f"Wrote {len(output)} predictions to {arguments.output}")


if __name__ == "__main__":
    main()
