"""Censored next-event models. This module does not approximate game physics."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from time import perf_counter_ns

import joblib
import numpy as np
import pandas as pd
from xgboost import XGBClassifier

from .constants import action_id_to_class
from .data import load_match_logs
from .features import FEATURE_COLUMNS, build_feature_frame
from .split import make_session_split


def event_targets(features):
    """Stop observation at gaps or non-voluntary states; never label censoring as an exit."""
    f = features.reset_index(drop=True)
    cls = f.self_action_id.map(action_id_to_class).to_numpy()
    duration = np.zeros(len(f), dtype=int)
    destination = np.full(len(f), -1, dtype=int)
    observed = np.zeros(len(f), dtype=bool)
    age = np.zeros(len(f), dtype=int)
    for _, group in f.groupby(['match_id', 'player_id'], sort=False):
        ids = group.index.to_numpy()
        for i, j in zip(ids[:-1], ids[1:]):
            if f.at[j, 'frame'] == f.at[i, 'frame'] + 1 and cls[i] == cls[j] and cls[i] >= 0:
                age[j] = age[i] + 1
        for i, j in zip(ids[-2::-1], ids[:0:-1]):
            if f.at[j, 'frame'] != f.at[i, 'frame'] + 1 or min(cls[i], cls[j]) < 0:
                continue
            if cls[i] != cls[j]:
                duration[i], destination[i], observed[i] = 1, cls[j], True
            else:
                duration[i] = duration[j] + 1
                destination[i], observed[i] = destination[j], observed[j]
    return pd.DataFrame(dict(current=cls, age=age, duration=duration,
                             destination=destination, observed=observed))


def risk_rows(targets, indices, cap=12):
    rows, steps, labels, weights = [], [], [], []
    for i in indices:
        t = targets.iloc[i]
        n = min(int(t.duration), cap)
        if t.current < 0 or n == 0:
            continue
        for k in range(1, n + 1):
            rows.append(i)
            steps.append(k)
            labels.append(int(t.observed and k == t.duration))
            weights.append(1 / max(1, int(t.age + t.duration)))
    return np.asarray(rows, int), np.asarray(steps), np.asarray(labels), np.asarray(weights)


class EventModel:
    def fit(self, x, targets, indices, exit_weight=1):
        rows, steps, y, weights = risk_rows(targets, indices)
        if len(np.unique(y)) < 2:
            raise ValueError('Training requires both observed exits and continuations')
        kwargs = dict(n_estimators=80, max_depth=4, learning_rate=.08,
                      tree_method='hist', n_jobs=2, random_state=42)
        self.hazard = XGBClassifier(**kwargs)
        self.hazard.fit(np.column_stack((x[rows], steps)), y,
                        sample_weight=weights * np.where(y == 1, exit_weight, 1))
        dest_rows = np.asarray([i for i in indices if targets.iloc[i].observed and targets.iloc[i].current >= 0], int)
        self.classes = np.unique(targets.destination.to_numpy()[dest_rows])
        if len(self.classes) < 2:
            raise ValueError('Training requires at least two exit destinations')
        self.destination = XGBClassifier(**kwargs)
        self.destination.fit(np.column_stack((x[dest_rows], targets.duration.to_numpy()[dest_rows])),
                             np.searchsorted(self.classes, targets.destination.to_numpy()[dest_rows]),
                             sample_weight=1 / np.maximum(1, (targets.age + targets.duration).to_numpy()[dest_rows]))
        return self

    def predict(self, x, current, k=1):
        values = np.column_stack((x, np.full(len(x), k)))
        h = self.hazard.predict_proba(values)[:, 1]
        p = np.zeros((len(x), 5))
        p[:, self.classes] = self.destination.predict_proba(values)
        valid = current >= 0
        p[np.flatnonzero(valid), current[valid]] = 0
        total = p.sum(axis=1, keepdims=True)
        p = np.divide(p, total, out=np.zeros_like(p), where=total > 0)
        p *= h[:, None]
        p[np.flatnonzero(valid), current[valid]] += 1 - h[valid]
        return p


def benchmark_next_frame(model, x, current, samples=500):
    """Warm single-row calls: feature extraction, I/O and game stepping excluded."""
    if not len(x):
        raise ValueError('No benchmark samples')
    for i in range(20):
        j = i % len(x)
        model.predict(x[j:j+1], current[j:j+1])
    elapsed = []
    for j in np.arange(min(samples, len(x))):
        start = perf_counter_ns()
        model.predict(x[j:j+1], current[j:j+1])
        elapsed.append((perf_counter_ns() - start) / 1e6)
    return dict(samples=len(elapsed), mean_ms=float(np.mean(elapsed)),
                p50_ms=float(np.median(elapsed)), p95_ms=float(np.percentile(elapsed, 95)),
                scope='warm single-row hazard + destination inference; excludes features and simulation')


def metrics(truth, pred, current):
    change = truth != current
    alert = pred != current
    def mean(v):
        return float(np.mean(v)) if len(v) else None
    return dict(samples=len(truth), accuracy=mean(pred == truth),
                change_accuracy=mean((pred == truth)[change]),
                missed_change_rate=mean((~alert)[change]),
                false_alarm_rate=mean(alert[~change]))


def run(logs, output):
    out = Path(output)
    out.mkdir(parents=True, exist_ok=True)
    data = load_match_logs(logs)
    f = build_feature_frame(data.frames, data.events)
    targets = event_targets(f)
    split = make_session_split(f.session_id)
    subsets = f.session_id.map(split).to_numpy()
    x = np.column_stack((f[FEATURE_COLUMNS].to_numpy(np.float32), targets.age)).astype(np.float32)
    current = targets.current.to_numpy()
    # Only exact observed next frames are scored; censoring boundaries are excluded.
    eligible = (targets.duration.to_numpy() > 0) & (current >= 0)
    truth = np.where(targets.observed & targets.duration.eq(1), targets.destination, current)
    train = np.flatnonzero(subsets == 'train')
    val = np.flatnonzero((subsets == 'validation') & eligible)
    test = np.flatnonzero((subsets == 'test') & eligible)
    if not len(val) or not len(test):
        raise ValueError('Validation and test need eligible next-frame rows')
    candidates = []
    models = []
    for weight in (1, 3):
        model = EventModel().fit(x, targets, train, weight)
        p = model.predict(x[val], current[val])
        for threshold in (.25, .5, .75):
            q = p.copy()
            q[np.arange(len(val)), current[val]] = 0
            change_prob = q.sum(axis=1)
            pred = np.where(change_prob >= threshold, q.argmax(axis=1), current[val])
            candidates.append(dict(weight=weight, threshold=threshold, **metrics(truth[val], pred, current[val])))
            models.append(model)
    # Predeclared 10% false-alarm constraint; fallback prioritizes lowest false alarms.
    allowed = [i for i, c in enumerate(candidates) if (c['false_alarm_rate'] or 0) <= .10]
    selected = max(allowed, key=lambda i: (candidates[i]['change_accuracy'] or 0, candidates[i]['accuracy'])) if allowed else min(range(len(candidates)), key=lambda i: candidates[i]['false_alarm_rate'])
    model, config = models[selected], candidates[selected]
    p = model.predict(x[test], current[test])
    p[np.arange(len(test)), current[test]] = 0
    pred = np.where(p.sum(axis=1) >= config['threshold'], p.argmax(axis=1), current[test])
    report = dict(status='offline next-event prototype; game replay and sequence evaluation pending',
                  split=split, feature_names=FEATURE_COLUMNS + ['class_age'],
                  validation=candidates, selected=config,
                  test=metrics(truth[test], pred, current[test]),
                  persistence=metrics(truth[test], current[test], current[test]),
                  next_frame_prediction_time=benchmark_next_frame(model, x[test], current[test]),
                  per_session={s: metrics(truth[test][f.session_id.iloc[test].to_numpy() == s], pred[f.session_id.iloc[test].to_numpy() == s], current[test][f.session_id.iloc[test].to_numpy() == s]) for s in sorted(f.session_id.iloc[test].unique())},
                  caveat='Existing test sessions informed the plan; independent new sessions required. No HP/state results claimed.')
    joblib.dump(dict(model=model, threshold=config['threshold'], feature_names=report['feature_names']), out / 'event_model.joblib')
    (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('test', 'persistence', 'next_frame_prediction_time')}, indent=2))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--logs', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    run(args.logs, args.output)


if __name__ == '__main__':
    main()
