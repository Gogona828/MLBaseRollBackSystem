"""Independently audit saved predictions, metrics, provenance and OOF splits."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from .constants import HORIZONS, action_id_to_class
from .data import load_match_logs
from .evaluation import evaluate_probabilities
from .features import build_feature_frame, build_supervised_dataset


def audit(output):
    output = Path(output)
    metadata = json.loads((output / "metadata.json").read_text())
    rows = json.loads((output / "metrics.json").read_text())
    assignment = json.loads((output / "split_assignment.json").read_text())
    assert metadata["logs_sha256"] == hashlib.sha256(Path(metadata["logs"]).read_bytes()).hexdigest()
    assert len(rows) == 160, len(rows)
    logs = load_match_logs(metadata["logs"])
    features = build_feature_frame(logs.frames, logs.events)
    assert len(features) == metadata["frame_rows"]
    groups = {part: set(s for s, p in assignment.items() if p == part) for part in ("train", "validation", "test")}
    assert not (groups["train"] & groups["validation"] | groups["train"] & groups["test"] | groups["validation"] & groups["test"])
    covered = []
    for fold in metadata["oof_folds"]:
        t, v, h = (set(fold[k]) for k in ("train", "inner_validation", "holdout"))
        assert not (t & v | t & h | v & h)
        assert t | v | h == groups["train"]
        covered.extend(h)
    assert set(covered) == groups["train"] and len(covered) == len(set(covered))
    checked = 0
    for horizon, (_, h) in HORIZONS.items():
        data = build_supervised_dataset(features, h)
        tr = data.index[data.session_id.isin(groups["train"])].to_numpy()
        te = data.index[data.session_id.isin(groups["test"])].to_numpy()
        saved = np.load(output / f"predictions_{horizon}.npz", allow_pickle=False)
        np.testing.assert_array_equal(saved["frame_indices"], te)
        np.testing.assert_array_equal(saved["targets"], data.loc[te, "target_class"])
        np.testing.assert_array_equal(saved["current"], features.loc[te, "self_action_id"].map(action_id_to_class))
        np.testing.assert_array_equal(saved["sessions"], features.loc[te, "session_id"])
        oof = np.load(output / f"oof_{horizon}.npz", allow_pickle=False)
        np.testing.assert_array_equal(oof["frame_indices"], tr)
        np.testing.assert_array_equal(oof["targets"], data.loc[tr, "target_class"])
        assert oof["probabilities"].shape == (len(tr), 10)
        assert np.isfinite(oof["probabilities"]).all()
        for offset in (0, 5):
            np.testing.assert_allclose(oof["probabilities"][:, offset:offset + 5].sum(1), 1)
        selected = [r for r in rows if r["horizon"] == horizon and r["split"] == "test"]
        assert len(selected) == 10 and len({r["model"] for r in selected}) == 10
        for row in selected:
            p = saved[row["model"]]
            assert p.shape == (len(te), 5) and np.isfinite(p).all() and (p >= 0).all()
            np.testing.assert_allclose(p.sum(1), 1)
            recalculated = evaluate_probabilities(saved["targets"], p, saved["current"])
            for key, value in recalculated.items():
                np.testing.assert_allclose(value, row[key], atol=1e-12)
            checked += 1
    reproduction = {h: v["baseline_max_probability_error"] for h, v in metadata["horizons"].items()}
    assert max(reproduction.values()) < 1e-7
    assert metadata["fep_reproduction_max_abs_error"] < 1e-7
    result = {"status": "passed", "test_model_horizon_results_recomputed": checked,
              "metric_records": len(rows), "oof_session_coverage": len(covered),
              "saved_prediction_shapes_and_normalization": "passed",
              "session_disjointness_and_inner_fold_exclusion": "passed",
              "source_hash_and_exact_target_alignment": "passed",
              "xgboost_reproduction_max_probability_error": max(reproduction.values()),
              "fep_reproduction_max_A_error": metadata["fep_reproduction_max_abs_error"]}
    (output / "verification.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", nargs="?", default="artifacts/hybrid_experiments")
    audit(parser.parse_args().output)
