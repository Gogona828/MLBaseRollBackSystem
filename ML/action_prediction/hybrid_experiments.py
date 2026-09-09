"""Reproducible session-held-out FEP hybrid and duration experiments.

Run from ML: .venv/bin/python -m action_prediction.hybrid_experiments
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import time
from datetime import datetime
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from catboost import CatBoostClassifier
from scipy.sparse import csr_matrix
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import GroupKFold

from .active_inference import (
    _entropy, _normalize, active_inference_probabilities,
    fit_active_inference_model, infer_frame_beliefs, load_active_inference_model,
    tune_scoring_parameters,
)
from .constants import ACTION_CLASSES, HORIZONS, action_id_to_class
from .data import load_match_logs
from .evaluation import evaluate_probabilities
from .features import FEATURE_COLUMNS, build_feature_frame, build_supervised_dataset
from .train import _new_model


def probabilities(model, x):
    p = np.zeros((len(x), 5))
    p[:, np.asarray(model.classes_, dtype=int)] = model.predict_proba(x)
    return p


def generative(model, beliefs, horizon):
    return _normalize(beliefs @ np.linalg.matrix_power(model.transition, horizon).T
                      @ model.observation_likelihood.T, axis=1)


def belief_features(model, beliefs, horizon):
    """Untuned features: validation labels cannot enter training features."""
    projected = beliefs @ np.linalg.matrix_power(model.transition, horizon).T
    efe = active_inference_probabilities(model, beliefs, horizon)
    return np.column_stack((beliefs, projected, efe, _entropy(beliefs), _entropy(efe)))


def compose_two_stage(change, destination, current):
    p = destination.copy()
    valid = current >= 0
    p[np.flatnonzero(valid), current[valid]] = 0
    p = _normalize(p, axis=1)
    change = np.where(valid, change, 1.0)
    p *= change[:, None]
    p[np.flatnonzero(valid), current[valid]] += 1 - change[valid]
    return _normalize(p, axis=1)


def causal_age(features, cap=180):
    classes = features.self_action_id.map(action_id_to_class).to_numpy()
    age = np.zeros(len(features), dtype=int)
    for _, indices in features.groupby(["match_id", "player_id"], sort=False).groups.items():
        previous = None
        for i in indices:
            if previous is not None and features.at[i, "frame"] == features.at[previous, "frame"] + 1 and classes[i] == classes[previous]:
                age[i] = min(age[previous] + 1, cap - 1)
            previous = i
    return classes, age


def fit_duration(features, train_mask, cap=180, strength=10.0):
    """Observed-state HSMM, equivalently identity-emission age-expanded HMM.

    Model all 5 voluntary states plus forced/unknown as state 5. Right-censored
    terminal rows add no exit or exposure. Durations >= cap share a tail hazard.
    """
    classes, age = causal_age(features, cap)
    states = np.where(classes >= 0, classes, 5)
    exposure = np.zeros((6, cap))
    exits = np.zeros((6, cap))
    jumps = np.ones((6, 6)) - np.eye(6)
    for _, indices in features.groupby(["match_id", "player_id"], sort=False).groups.items():
        indices = np.asarray(indices)
        for i, j in zip(indices[:-1], indices[1:]):
            if not train_mask[i] or features.at[j, "frame"] != features.at[i, "frame"] + 1:
                continue
            s, dest = states[i], states[j]
            exposure[s, age[i]] += 1
            if dest != s:
                exits[s, age[i]] += 1
                jumps[s, dest] += 1
    base = (exits.sum(axis=1) + 1) / (exposure.sum(axis=1) + 20)
    hazard = (exits + strength * base[:, None]) / (exposure + strength)
    jumps = _normalize(jumps, axis=1)
    rows, cols, values = [], [], []
    for s in range(6):
        for a in range(cap):
            src = s * cap + a
            rows.append(src); cols.append(s * cap + min(a + 1, cap - 1)); values.append(1 - hazard[s, a])
            for d in range(6):
                if d != s:
                    rows.append(src); cols.append(d * cap); values.append(hazard[s, a] * jumps[s, d])
    transition = csr_matrix((values, (rows, cols)), shape=(6 * cap, 6 * cap))
    # Dynamic programming computes endpoint distributions for every start state.
    lookup = np.repeat(np.eye(6), cap, axis=0)
    forecasts = {}
    wanted = {h for _, h in HORIZONS.values()}
    for step in range(1, max(wanted) + 1):
        lookup = transition @ lookup
        if step in wanted:
            forecasts[step] = _normalize(lookup[states * cap + age, :5], axis=1)
    return forecasts, {"hazard": hazard, "jumps": jumps, "cap": cap,
                       "exposure": exposure, "exits": exits, "strength": strength}


def context_ids(features, edges):
    distance = np.digitize(features.absolute_distance.to_numpy(), edges)
    opponent = features.opponent_action_id.map(action_id_to_class).to_numpy() + 1
    corner = features.self_is_cornered.to_numpy().astype(int)
    return distance * 12 + opponent * 2 + corner


def fit_context_transitions(features, train_mask, model, beliefs, strength):
    """One-pass local posterior pair counts, shrunk toward global B.

    Counts are P(s_t,s_t+1 | observations through t+1, global fitted A/B).
    This is a context-binned approximate fit, not jointly optimized EM.
    """
    edges = np.unique(np.quantile(features.loc[train_mask, "absolute_distance"], [1/3, 2/3]))
    contexts = context_ids(features, edges)
    count = (len(edges) + 1) * 12
    matrices = np.repeat((strength * model.transition)[None], count, axis=0)
    obs = features.self_action_id.map(action_id_to_class).to_numpy()
    for _, indices in features.groupby(["match_id", "player_id"], sort=False).groups.items():
        indices = np.asarray(indices)
        for i, j in zip(indices[:-1], indices[1:]):
            if not train_mask[i] or features.at[j, "frame"] != features.at[i, "frame"] + 1:
                continue
            pair = model.transition * beliefs[i][None, :]
            if obs[j] >= 0:
                pair *= model.observation_likelihood[obs[j]][:, None]
            matrices[contexts[i]] += pair / pair.sum()
    matrices /= matrices.sum(axis=1, keepdims=True)
    filtered = np.zeros_like(beliefs)
    for _, indices in features.groupby(["match_id", "player_id"], sort=False).groups.items():
        b = model.initial_belief.copy()
        previous = None
        for i in indices:
            b = (model.transition if previous is None else matrices[contexts[previous]]) @ b
            if obs[i] >= 0:
                b = _normalize(b * model.observation_likelihood[obs[i]])
            filtered[i] = b
            previous = i
    forecasts = {}
    for _, h in HORIZONS.values():
        projected = np.empty_like(beliefs)
        for c in range(count):
            mask = contexts == c
            projected[mask] = filtered[mask] @ np.linalg.matrix_power(matrices[c], h).T
        forecasts[h] = _normalize(projected @ model.observation_likelihood.T, axis=1)
    return forecasts, {"edges": edges, "matrices": matrices, "strength": strength}


def fit_xgb(x, y, train, validation, estimators=500, binary=False):
    labels = np.unique(y[train])
    encoded = np.searchsorted(labels, y)
    model = _new_model(42, estimators, 6)
    if binary:
        model.set_params(objective="binary:logistic", num_class=None, eval_metric="logloss")
    use_validation = validation[np.isin(y[validation], labels)]
    if len(use_validation) == 0:
        raise ValueError("No validation labels represented in training")
    model.fit(x[train], encoded[train], eval_set=[(x[use_validation], encoded[use_validation])], verbose=False)
    return model, labels


def predict_encoded(pair, x):
    model, labels = pair
    p = np.zeros((len(x), 5))
    p[:, labels] = model.predict_proba(x)
    return p


def bootstrap_delta(y, p, reference, sessions, repetitions=1000):
    """Paired session-cluster bootstrap for accuracy difference."""
    groups = np.unique(sessions)
    n = np.array([(sessions == s).sum() for s in groups])
    delta = (p.argmax(1) == y).astype(float) - (reference.argmax(1) == y)
    sums = np.array([delta[sessions == s].sum() for s in groups])
    sampled = np.random.default_rng(42).integers(len(groups), size=(repetitions, len(groups)))
    values = sums[sampled].sum(1) / n[sampled].sum(1)
    return np.quantile(values, [0.025, 0.975]).tolist()


def run(args):
    started = time.time()
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    models = output / "models"
    models.mkdir(exist_ok=True)
    assignment = json.loads((Path(args.baseline) / "split_assignment.json").read_text())
    logs = load_match_logs(args.logs)
    features = build_feature_frame(logs.frames, logs.events)
    split = features.session_id.map(assignment)
    if split.isna().any():
        raise ValueError("Logs contain sessions outside frozen baseline split")
    # Existing filtering assumes contiguous frames; fail rather than bridge gaps.
    diffs = features.groupby(["match_id", "player_id"]).frame.diff().dropna()
    if not diffs.eq(1).all():
        raise ValueError("Noncontiguous frames require gap-aware FEP filtering")
    train_mask = split.eq("train").to_numpy()
    meta = {"seed": 42, "started_at": datetime.now().astimezone().isoformat(), "logs": str(Path(args.logs).resolve()),
            "logs_sha256": hashlib.sha256(Path(args.logs).read_bytes()).hexdigest(),
            "frame_rows": len(features), "feature_names": FEATURE_COLUMNS,
            "session_counts": {s: int(features.loc[split.eq(s), "session_id"].nunique()) for s in ("train", "validation", "test")},
            "versions": {p: importlib.metadata.version(p) for p in ("numpy", "pandas", "scipy", "scikit-learn", "xgboost", "catboost")},
            "horizons": {}}
    (output / "split_assignment.json").write_text(json.dumps(assignment, indent=2))
    print("Fit global FEP on training sessions", flush=True)
    fep = fit_active_inference_model(features, assignment)
    fep.save(models / "fep.npz")
    beliefs = infer_frame_beliefs(features, fep)
    old_fep = load_active_inference_model(Path(args.baseline) / "models/model_active_inference.npz")
    meta["fep_reproduction_max_abs_error"] = float(np.max(np.abs(fep.observation_likelihood - old_fep.observation_likelihood)))
    # Prepare 3 outer OOF fits once. Early stopping/scoring use an inner session holdout.
    training_sessions = np.array(sorted(s for s, part in assignment.items() if part == "train"))
    fold_data = []
    for fold, (remaining, holdout) in enumerate(GroupKFold(3).split(training_sessions, groups=training_sessions)):
        remaining = np.random.default_rng(42 + fold).permutation(training_sessions[remaining])
        inner_val = set(remaining[:max(1, len(remaining) // 5)])
        inner_train = set(remaining) - inner_val
        held = set(training_sessions[holdout])
        fold_assignment = {s: "train" if s in inner_train else "validation" if s in inner_val else "test" for s in assignment}
        print(f"Fit OOF FEP {fold + 1}/3 ({len(inner_train)} train sessions)", flush=True)
        fm = fit_active_inference_model(features, fold_assignment)
        fm.save(models / f"fep_oof_{fold}.npz")
        fb = infer_frame_beliefs(features, fm)
        fold_data.append((inner_train, inner_val, held, fm, fb))
    meta["oof_folds"] = [{"train": sorted(t), "inner_validation": sorted(v), "holdout": sorted(h)} for t, v, h, _, _ in fold_data]
    print("Fit duration and context-conditioned models", flush=True)
    duration = {s: fit_duration(features, train_mask, strength=s) for s in (2.0, 10.0, 50.0)}
    context = {s: fit_context_transitions(features, train_mask, fep, beliefs, s) for s in (10.0, 100.0, 1000.0)}
    joblib.dump({"duration": {s: x[1] for s, x in duration.items()}, "context": {s: x[1] for s, x in context.items()}}, models / "structured_models.joblib")
    raw = features[FEATURE_COLUMNS].to_numpy(dtype=np.float32)
    categorical = [c for c in FEATURE_COLUMNS if "action_id" in c or "action_group" in c or c in ("input_bits", "input_down_bits", "input_up_bits")]
    cat = features[FEATURE_COLUMNS].copy()
    for c in categorical:
        cat[c] = cat[c].fillna(-1).astype(int).astype(str)
    rows, prediction_files = [], []
    for horizon, (_, h) in HORIZONS.items():
        print(f"[{horizon}] Train XGBoost, hybrid, two-stage, CatBoost, stacking", flush=True)
        data = build_supervised_dataset(features, h)
        parts = data.session_id.map(assignment)
        ids = {s: data.index[parts.eq(s)].to_numpy() for s in ("train", "validation", "test")}
        tr, va, te = (ids[s] for s in ("train", "validation", "test"))
        y = np.full(len(features), -1, dtype=int)
        y[data.index] = data.target_class
        current = features.self_action_id.map(action_id_to_class).to_numpy()
        cfg = {}
        params, _ = tune_scoring_parameters(fep, beliefs[va], y[va], h)
        predictions = {"fep_efe": active_inference_probabilities(fep, beliefs, h, **params),
                       "fep_generative": generative(fep, beliefs, h)}
        cfg["efe_parameters"] = params
        xgb = fit_xgb(raw, y, tr, va)
        predictions["xgboost"] = predict_encoded(xgb, raw)
        xgb[0].save_model(models / f"xgboost_{horizon}.json")
        hybrid_x = np.column_stack((raw, belief_features(fep, beliefs, h))).astype(np.float32)
        hybrid = fit_xgb(hybrid_x, y, tr, va)
        predictions["xgboost_fep"] = predict_encoded(hybrid, hybrid_x)
        hybrid[0].save_model(models / f"xgboost_fep_{horizon}.json")
        # Keep a no-FEP ablation so effects of staging and belief features separate.
        for name, values in (("two_stage", raw), ("two_stage_fep", hybrid_x)):
            changed = (current != y).astype(int)
            gate = fit_xgb(values, changed, tr, va, binary=True)
            destination = fit_xgb(values, y, tr[changed[tr] == 1], va[changed[va] == 1])
            p_change = gate[0].predict_proba(values)[:, np.flatnonzero(gate[1] == 1)[0]]
            predictions[name] = compose_two_stage(p_change, predict_encoded(destination, values), current)
            joblib.dump((gate, destination), models / f"{name}_{horizon}.joblib")
        cb = CatBoostClassifier(iterations=500, depth=6, learning_rate=0.05, loss_function="MultiClass",
                                random_seed=42, thread_count=4, allow_writing_files=False, verbose=False,
                                cat_features=categorical, early_stopping_rounds=30)
        cb.fit(cat.iloc[tr], y[tr], eval_set=(cat.iloc[va], y[va]))
        predictions["catboost"] = probabilities(cb, cat)
        cb.save_model(str(models / f"catboost_{horizon}.cbm"))
        cfg["best_iterations"] = {"xgboost": int(xgb[0].best_iteration), "xgboost_fep": int(hybrid[0].best_iteration), "catboost": int(cb.best_iteration_)}
        for name, candidates in (("hsmm_duration", duration), ("context_B", context)):
            losses = {s: float(-np.log(np.clip(value[0][h][va, y[va]], 1e-15, 1)).mean()) for s, value in candidates.items()}
            selected = min(losses, key=losses.get)
            predictions[name] = candidates[selected][0][h]
            cfg[name] = {"strength": selected, "validation_losses": losses}
        # Strict session OOF predictions. No test/outer-held labels tune a base model.
        oof = np.full((len(features), 10), np.nan)
        for fold, (it, iv, held, fm, fb) in enumerate(fold_data):
            ft = data.index[data.session_id.isin(it)].to_numpy()
            fv = data.index[data.session_id.isin(iv)].to_numpy()
            fh = data.index[data.session_id.isin(held)].to_numpy()
            base = fit_xgb(raw, y, ft, fv)
            fp, _ = tune_scoring_parameters(fm, fb[fv], y[fv], h)
            oof[fh] = np.column_stack((predict_encoded(base, raw[fh]), active_inference_probabilities(fm, fb[fh], h, **fp)))
            base[0].save_model(models / f"stack_base_oof{fold}_{horizon}.json")
            cfg[f"oof_{fold}"] = {"parameters": fp, "best_iteration": int(base[0].best_iteration)}
        assert np.isfinite(oof[tr]).all()
        stack_x = np.column_stack((predictions["xgboost"], predictions["fep_efe"]))
        stack_candidates = []
        for c in (0.1, 1.0, 10.0):
            sm = LogisticRegression(C=c, max_iter=2000, random_state=42)
            sm.fit(oof[tr], y[tr])
            sp = probabilities(sm, stack_x)
            loss = -np.log(np.clip(sp[va, y[va]], 1e-15, 1)).mean()
            stack_candidates.append((float(loss), c, sm, sp))
        _, c, sm, sp = min(stack_candidates, key=lambda item: item[0])
        cfg["stacking_C"] = c
        predictions["stacking"] = sp
        joblib.dump(sm, models / f"stacking_{horizon}.joblib")
        np.savez_compressed(output / f"oof_{horizon}.npz", frame_indices=tr, probabilities=oof[tr], targets=y[tr])
        session_test = features.session_id.iloc[te].to_numpy()
        # Explicitly check reproduction of the frozen original XGBoost baseline.
        from xgboost import XGBClassifier
        original = XGBClassifier()
        original.load_model(Path(args.baseline) / "models" / f"model_{horizon}.json")
        cfg["baseline_max_probability_error"] = float(np.max(np.abs(probabilities(original, raw[te]) - predictions["xgboost"][te])))
        for name, p in predictions.items():
            if not np.isfinite(p).all() or (p < 0).any() or not np.allclose(p.sum(1), 1):
                raise ValueError(f"Invalid probabilities: {horizon}/{name}")
            for part in ("validation", "test"):
                ix = ids[part]
                metrics = evaluate_probabilities(y[ix], p[ix], current[ix])
                row = {"horizon": horizon, "frames": h, "model": name, "split": part, "samples": len(ix), **metrics}
                if part == "test":
                    row["accuracy_delta_ci95"] = bootstrap_delta(y[te], p[te], predictions["xgboost"][te], session_test)
                rows.append(row)
        prediction_path = output / f"predictions_{horizon}.npz"
        np.savez_compressed(prediction_path, frame_indices=te, targets=y[te], current=current[te], sessions=session_test.astype(str),
                            **{n: p[te] for n, p in predictions.items()})
        prediction_files.append(prediction_path.name)
        cfg["samples"] = {s: len(ix) for s, ix in ids.items()}
        meta["horizons"][horizon] = cfg
        # Write checkpoints so an interrupted run can be audited.
        (output / "metrics.json").write_text(json.dumps(rows, indent=2))
        (output / "metadata.json").write_text(json.dumps(meta, indent=2))
        print(f"[{horizon}] complete: " + ", ".join(f"{n}={evaluate_probabilities(y[te], p[te])['accuracy']:.3f}" for n, p in predictions.items()), flush=True)
    meta["elapsed_seconds"] = time.time() - started
    meta["prediction_files"] = prediction_files
    (output / "metadata.json").write_text(json.dumps(meta, indent=2))
    flat = [{k: v for k, v in row.items() if k not in ("confusion_matrix", "accuracy_delta_ci95")} for row in rows]
    pd.DataFrame(flat).to_csv(output / "metrics_summary.csv", index=False)
    write_report(output, rows, meta)
    print(f"Completed in {meta['elapsed_seconds']:.1f}s: {output / 'experiment_report.md'}", flush=True)


def write_report(output, rows, meta):
    lookup = {(r["horizon"], r["model"], r["split"]): r for r in rows}
    names = list(dict.fromkeys(r["model"] for r in rows))
    winners = {h: min(names, key=lambda n: lookup[h, n, "validation"]["log_loss"]) for h in HORIZONS}
    hybrid_acc_wins = sum(lookup[h, "xgboost_fep", "test"]["accuracy"] > lookup[h, "xgboost", "test"]["accuracy"] for h in HORIZONS)
    hybrid_ll_wins = sum(lookup[h, "xgboost_fep", "test"]["log_loss"] < lookup[h, "xgboost", "test"]["log_loss"] for h in HORIZONS)
    def value(h, name, metric="accuracy"):
        return lookup[h, name, "test"][metric]

    def comparison(h, name, metric="accuracy", reference="xgboost"):
        before, after = value(h, reference, metric), value(h, name, metric)
        if "accuracy" in metric:
            return f"{before * 100:.2f}%→{after * 100:.2f}%"
        return f"{before:.4f}→{after:.4f}"

    def wins(name, reference, metric, sign):
        return sum((value(h, name, metric) - value(h, reference, metric)) * sign > 0 for h in HORIZONS)

    stack_counts = np.asarray(lookup["3s", "stacking", "test"]["confusion_matrix"]).sum(axis=0)
    stack_distribution = "、".join(f"{a} {n}件" for a, n in zip(ACTION_CLASSES, stack_counts))
    lo300, hi300 = lookup["300ms", "xgboost_fep", "test"]["accuracy_delta_ci95"]
    run_date = meta.get("started_at", datetime.fromtimestamp((output / "split_assignment.json").stat().st_mtime).astimezone().isoformat())
    lines = ["# FEPハイブリッド比較実験", "", f"実行日: {run_date}", "",
             "## 実行結果の要点", "",
             "前回提案した6段階を実行し、対照を含む10方式×8ホライズンの80比較を完了した。各方式についてvalidation/testの両方を記録している。",
             f"FEP信念特徴を加えたXGBoostは、基準に対してtest Accuracyが8条件中{hybrid_acc_wins}条件、Log Lossが{hybrid_ll_wins}条件で改善した。したがって、FEP特徴追加の有効性はホライズン別に判断する必要がある。",
             "validation Log Lossによる選択: " + "、".join(f"{h}={name}" for h, name in winners.items()) + "。",
             "小さな精度差はセッション間の変動も確認する。下表の信頼区間がゼロをまたぐ場合、このデータでは基準より改善したと明確に区別できない。", "",
             "今回の優先候補は、確率予測の改善を狙う場合のFEP特徴付きXGBoost。前回推奨した二段式＋FEPの一律採用を支持する結果にはならなかった。",
             "", "## 具体的な所見", "",
             f"1. 純粋な生成予測は既存EFEより{wins('fep_generative', 'fep_efe', 'log_loss', -1)}/8条件でLog Lossを改善した。50ms Accuracyは{comparison('50ms', 'fep_generative', reference='fep_efe')}。固定Utilityを介すことの影響を検証する結果として、観測予測と方策選択を分けて扱う根拠になる。",
             f"2. FEP特徴追加は200ms Accuracy {comparison('200ms', 'xgboost_fep')}、Macro F1 {comparison('200ms', 'xgboost_fep', 'macro_f1')}、300ms Accuracy {comparison('300ms', 'xgboost_fep')}となった。300msのAccuracy差の区間は{lo300*100:+.2f}〜{hi300*100:+.2f}ポイント。100msでは{comparison('100ms', 'xgboost_fep')}。これは多重比較補正前の探索結果である。",
             f"3. 二段式＋FEPは1秒の変化ありAccuracy {comparison('1s', 'two_stage_fep', 'changed_action_accuracy')}、全体Accuracy {comparison('1s', 'two_stage_fep')}、Log Loss {comparison('1s', 'two_stage_fep', 'log_loss')}となった。変化を当てることを優先する場合は、全体精度や確率品質との交換条件を検討する。2秒の全体Accuracyは{comparison('2s', 'two_stage_fep')}。",
             f"4. CatBoostのtest Accuracy改善はXGBoostに対して{wins('catboost', 'xgboost', 'accuracy', 1)}/8条件。2秒のMacro F1は{comparison('2s', 'catboost', 'macro_f1')}、Log Lossは{comparison('2s', 'catboost', 'log_loss')}である。validationでの選択はtestで複数指標が改善する保証ではない。",
             f"5. 確率スタッキングのtest Log Loss改善はXGBoostに対して{wins('stacking', 'xgboost', 'log_loss', -1)}/8条件。3秒のAccuracyは{value('3s', 'stacking')*100:.2f}%で、予測分布は{stack_distribution}。単一クラスに集中した場合は5クラス予測の改善と扱えない。OOFと最終ベースモデルの学習件数差も結果に影響しうる。",
             f"6. HSMMは行動履歴のみの生成予測に対して{wins('hsmm_duration', 'fep_generative', 'accuracy', 1)}/8条件のAccuracyと{wins('hsmm_duration', 'fep_generative', 'log_loss', -1)}/8条件のLog Lossを改善した。50msの変化ありAccuracyは{value('50ms', 'hsmm_duration', 'changed_action_accuracy')*100:.2f}%。文脈Bの生成予測に対するLog Loss改善は{wins('context_B', 'fep_generative', 'log_loss', -1)}/8条件で、長期に文脈を固定する制約が残る。",
             "", f"3秒ではモデル全体のMacro F1が最大でも{max(value('3s', n, 'macro_f1') for n in names):.3f}。採用判断はホライズン別・評価目的別に行い、新規セッションで再検証する必要がある。", "",
             "## 条件と実装", "",
             f"入力 {meta['frame_rows']:,} フレーム。セッション分割は既存の固定分割 {meta['session_counts']}。seed=42。",
             "全方式を50/100/200/300/500ms、1/2/3秒先の8ホライズンで実行。未来の終端行動を直接予測するオフライン実験であり、1フレーム自己回帰やUnity再シミュレーションではない。",
             "ターゲットはWait/Approach/Retreat/Attack/Guard。強制・終了状態が未来ラベルの行は既存仕様どおり除外。現在が強制状態の行は残す。",
             "XGBoost基準は既存48特徴、最大500木、depth=6、learning_rate=0.05、validation早期停止30。元モデルとの確率差をmetadataに記録。",
             "FEPはtrainのみで30回上限Baum–Welchを再実行。A/B/D/Eを学習。入力は現在までの行動履歴。Utility/Cueは既存固定値。",
             "既存EFEの情報価値はentropyを用いた近似スコアをそのまま維持している。厳密な期待事後情報利得を再実装した実験ではない。",
             "", "|方式|実装|", "|---|---|",
             "|fep_efe|既存EFE。3尺度をvalidation Log Lossで選択|",
             "|fep_generative|A B^H beliefによる観測予測。EFE不使用|",
             "|xgboost|既存設定を再学習した基準|",
             "|xgboost_fep|48特徴＋現在信念3＋投影信念3＋既定尺度EFE確率5＋信念/確率entropy2（計61）|",
             "|two_stage|既存48特徴で終端クラス変化の二値分類＋変化したサンプルの5クラス分類|",
             "|two_stage_fep|二段式へFEP特徴を追加|",
             "|catboost|同じ48特徴。行動ID/行動group/入力bitsをカテゴリ扱い。500回上限、depth6、早期停止30|",
             "|stacking|XGB5確率＋EFE5確率をロジスティック回帰。trainのsession 3-fold OOFで学習、Cはvalidation選択|",
             "|hsmm_duration|5行動＋強制・終了・不明をまとめた6状態、identity emissionを持つ観測状態HSMM。年齢180フレーム上限の離散hazard|",
             "|context_B|距離のtrain三分位×相手6行動区分×画面端の文脈別3×3 B。全体Bへ縮約|", "",
             "二段式の変化は現在クラスと終端クラスが異なること。途中で変化して元に戻るケースは変化なし。第2段は現在クラスをマスクして正規化。現在が強制状態なら変化確率を1とする。",
             "HSMMは行動系列の連続継続時間を現在までだけで計算し、年齢ごとの離脱率と離脱先分布をtrainで推定。末尾打切りの未観測遷移を数えない。180フレーム以上はtail hazardを共有。観測行動に対応する状態が既知の特殊ケースであり、自由な潜在状態をEM推定するHSMMではない。未来の強制状態確率を除き5クラスへ条件付けて評価。縮約強度2/10/50をvalidation選択。",
             "context_Bは全体FEP下でt+1までの観測による隣接状態事後分布を1回集計する近似学習。文脈別EMの反復最適化は行わない。実観測のフィルタは直前文脈Bで更新し、未来Hステップは予測開始時の文脈を固定する。縮約強度10/100/1000をvalidation選択。",
             "スタッキングの外側holdoutは学習・早期停止・EFE調整の全てから除外。各foldの残りセッション内で約20%を内部validationへ割り当て、FEP/XGBを再学習。最終ベースは全train＋通常validationを用いるため、OOFとの学習件数差がある。",
             "FEP特徴のEFE尺度は1/1/1固定で、validationラベルによる特徴生成を避けた。信念は未来を見ないfiltering。session単位で履歴が分離される。", "",
             "## Validationによる方式選択と独立test結果", "",
             "各ホライズンでvalidation Log Loss最小の方式を選択し、そのtest結果を下に掲載。test最良の方式を選んだ表ではない。",
             "", "|時間|選択方式|Val LL|Test Accuracy|Macro F1|Log Loss|変化ありAcc|基準Acc差 [session bootstrap 95%区間]|", "|---|---|---:|---:|---:|---:|---:|---|"]
    for h in HORIZONS:
        winner = min(names, key=lambda n: lookup[h, n, "validation"]["log_loss"])
        v, r, b = lookup[h, winner, "validation"], lookup[h, winner, "test"], lookup[h, "xgboost", "test"]
        lo, hi = r["accuracy_delta_ci95"]
        lines.append(f"|{h}|{winner}|{v['log_loss']:.4f}|{r['accuracy']:.4f}|{r['macro_f1']:.4f}|{r['log_loss']:.4f}|{r['changed_action_accuracy']:.4f}|{r['accuracy']-b['accuracy']:+.4f} [{lo:+.4f}, {hi:+.4f}]|")
    lines += ["", "## Test標本構成", "", "|時間|件数|変化あり|変化なし|Wait|Approach|Retreat|Attack|Guard|",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for h in HORIZONS:
        r = lookup[h, "xgboost", "test"]
        counts = np.asarray(r["confusion_matrix"]).sum(axis=1)
        lines.append(f"|{h}|{r['samples']}|{r['changed_action_samples']}|{r['unchanged_action_samples']}|" + "|".join(str(n) for n in counts) + "|")
    for metric, title in (("accuracy", "Accuracy"), ("macro_f1", "Macro F1"), ("log_loss", "Log Loss（低いほど良い）"), ("changed_action_accuracy", "変化ありAccuracy")):
        lines += ["", f"## Test {title}", "", "|方式|" + "|".join(HORIZONS) + "|", "|---|" + "---:|" * len(HORIZONS)]
        for name in names:
            lines.append("|" + name + "|" + "|".join(f"{lookup[h, name, 'test'][metric]:.4f}" for h in HORIZONS) + "|")
    lines += ["", "## 基準との差と解釈", "", "8ホライズンでの改善数。これは各ホライズンの差の記述で、有意性や未知セッションへの保証を意味しない。", "",
              "|方式|Accuracy改善数|Macro F1改善数|Log Loss改善数|", "|---|---:|---:|---:|"]
    for name in names:
        if name == "xgboost":
            continue
        counts = [sum((lookup[h, name, "test"][m] - lookup[h, "xgboost", "test"][m]) * sign > 0 for h in HORIZONS)
                  for m, sign in (("accuracy", 1), ("macro_f1", 1), ("log_loss", -1))]
        lines.append(f"|{name}|{counts[0]}/8|{counts[1]}/8|{counts[2]}/8|")
    lines += ["", "## 提案6段階の検証", "", "|段階|比較|Accuracy改善条件数|Log Loss改善条件数|", "|---|---|---:|---:|"]
    for stage, model, reference in (("1 生成予測", "fep_generative", "fep_efe"),
                                    ("3 信念特徴", "xgboost_fep", "xgboost"),
                                    ("4 二段式", "two_stage_fep", "xgboost_fep"),
                                    ("5 CatBoost", "catboost", "xgboost"),
                                    ("6 スタッキング", "stacking", "xgboost"),
                                    ("6 HSMM", "hsmm_duration", "fep_generative"),
                                    ("6 文脈B", "context_B", "fep_generative")):
        acc = sum(lookup[h, model, "test"]["accuracy"] > lookup[h, reference, "test"]["accuracy"] for h in HORIZONS)
        ll = sum(lookup[h, model, "test"]["log_loss"] < lookup[h, reference, "test"]["log_loss"] for h in HORIZONS)
        lines.append(f"|{stage}|{model} vs {reference}|{acc}/8|{ll}/8|")
    lines += ["", f"段階2の固定基準再現: 全8ホライズンのXGBoost確率最大絶対差={max(v['baseline_max_probability_error'] for v in meta['horizons'].values()):.3g}。FEP観測行列最大絶対差={meta['fep_reproduction_max_abs_error']:.3g}。",
              "FEP特徴は現在行動履歴の圧縮表現であり、距離などの新たな観測を追加するものではない。小さな改善だけで潜在戦術を捉えた証拠とは解釈できない。",
              "二段式は同じFEP特徴あり/なしの対照を併記した。現在クラスを継続する確率と異なる終端クラスの予測を分離しても、行動変化時の精度が必ず改善するとは限らない。",
              "HSMMの基準はまず同じ系列情報だけを使うFEP生成予測で見る。48の状況特徴を使うXGBoostとの比較には入力情報量の差が含まれる。文脈Bも距離・相手行動・画面端だけを追加した限定モデルである。"]
    lines += ["", "## 制約", "",
              "- 既存testを前回すでに確認してから設計した探索実験。testラベルを学習/調整には使用していないが、新規の未見データでの追試が必要。",
              "- testは8セッション。フレームは独立標本ではない。区間はセッション単位paired bootstrap 1,000回、Accuracy差のみ。多数方式/ホライズンの多重比較補正は行っていない。",
              "- 分割はsession単位であり、同一プレイヤーが複数sessionにいる場合の未知プレイヤー汎化は保証しない。",
              "- ログの現フレーム入力/状態を観測できる前提。通信未着時に実際に利用可能な入力だけでの評価は別途必要。未来Actionバッファは除外済み。",
              "- ホライズンごとに有効標本とクラス構成が変わる。異なる時間間のAccuracyを同一母集団とみなせない。",
              "- HSMMとcontext_Bはここに記載した具体的な初期実装の結果であり、各手法全般の性能上限ではない。",
              "- 全方式はオフライン直接ホライズン予測。rollout経路誤差、rollback回数、位置誤差、ネットワーク遅延、推論レイテンシは測っていない。",
              "- 深層系列モデルは前回の6段階の実行項目に含めていないため対象外。", "",
              "## 再現と成果物", "", "```sh", "cd ML",
              ".venv/bin/python -m action_prediction.hybrid_experiments --logs ../CombatGame/MatchLogs.zip --baseline artifacts --output artifacts/hybrid_experiments", "```", "",
              "- metrics_summary.csv / metrics.json: 全方式のvalidation/test指標、混同行列、test差の区間",
              "- predictions_*.npz: test予測確率・ラベル・元フレームindex・session",
              "- oof_*.npz: スタッキング学習用out-of-fold予測",
              "- models/: 学習済みモデル、HSMM hazard、文脈B、OOFベースモデル",
              "- metadata.json / split_assignment.json: 学習設定、fold、バージョン、入力SHA256、基準再現誤差",
              "- verification.json: 保存済み全80結果の再計算、ラベル整合性、OOF分割を独立監査した結果（audit_hybrid_experimentsで生成）",
              f"- 経過時間: {meta['elapsed_seconds']:.1f}秒", "",
              "監査コマンド: `.venv/bin/python -m action_prediction.audit_hybrid_experiments`。追加5件を含む全14件の単体テストで因果性、確率合成、系列末尾の打切り処理を確認。", "",
              "CatBoostカテゴリ指定は[公式ドキュメント](https://catboost.ai/docs/en/concepts/python-reference_catboostclassifier)、スタッキングの交差検証設計は[scikit-learn公式説明](https://scikit-learn.org/stable/modules/generated/sklearn.ensemble.StackingClassifier.html)を参照。", ""]
    (output / "experiment_report.md").write_text("\n".join(lines), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--logs", default="../CombatGame/MatchLogs.zip")
    parser.add_argument("--baseline", default="artifacts")
    parser.add_argument("--output", default="artifacts/hybrid_experiments")
    run(parser.parse_args())


if __name__ == "__main__":
    main()
