"""Offline 200-round sequence experiment; never reads future state during rollout."""
from __future__ import annotations

import argparse
import hashlib
import json
import platform
from pathlib import Path
from time import perf_counter_ns

import joblib
import numpy as np
import pandas as pd
from xgboost import XGBClassifier

from .active_inference import fit_active_inference_model, infer_frame_beliefs
from .constants import action_id_to_class
from .data import load_match_logs
from .features import build_feature_frame, FEATURE_COLUMNS
from .sequence import EventModel, event_targets
from .split import apply_session_split

HORIZONS = {'50ms': 3, '100ms': 6, '200ms': 12}
# Frozen exogenous context is explicitly separated from renewable action state.
CONTEXT = [n for n in FEATURE_COLUMNS if n.startswith(('opponent_', 'self_position', 'self_velocity',
           'self_vital', 'self_guard', 'self_is_', 'self_hitstun')) or n in
           ('absolute_distance', 'signed_distance_to_opponent', 'fixed_delta_time')]
DYNAMIC = ['current_class', 'class_age', 'previous_class_1', 'previous_class_2']


def features_for_events(f, targets):
    previous = np.column_stack([f[n].map(action_id_to_class) for n in
                                ('previous_action_id_1', 'previous_action_id_2')])
    return np.column_stack((targets.current, targets.age, previous,
                            f[CONTEXT].to_numpy())).astype(np.float32)


def paths_for(f, split, subset, horizon):
    """Only fully contiguous voluntary paths; report exclusions rather than score unknowns as correct."""
    paths = []
    attempted = 0
    cls = f.self_action_id.map(action_id_to_class).to_numpy()
    for _, g in f.groupby(['match_id', 'player_id'], sort=False):
        if split[str(g.session_id.iloc[0])] != subset:
            continue
        ids = g.index.to_numpy()
        for j in range(len(ids) - horizon):
            attempted += 1
            p = ids[j:j + horizon + 1]
            if np.all(np.diff(f.frame.to_numpy()[p]) == 1) and np.all(cls[p] >= 0):
                paths.append(p)
    if not paths:
        raise ValueError(f'No eligible {subset} paths for {horizon}')
    return np.asarray(paths), attempted - len(paths)


def rollout(model, initial, horizon, width=1, threshold=.5):
    """Beam of event-conditioned paths; score selection has no access to targets.

    Anchor x stays at the last predicted event, k increases while surviving.
    Upon exit, update class/age/history and reset k. Physical context and FEP
    belief stay at the observation time: these are offline action-only forecasts.
    """
    n = len(initial)
    anchors = initial[:, None, :].copy()
    steps = np.ones((n, 1), int)
    scores = np.zeros((n, 1))
    history = np.empty((n, 1, 0), int)
    for _ in range(horizon):
        b = anchors.shape[1]
        flat = anchors.reshape(-1, anchors.shape[-1])
        current = flat[:, 0].astype(int)
        p = model.predict(flat, current, steps.ravel())
        # Predeclared hazard odds adjustment. Scores are adjusted model scores,
        # not calibrated empirical probabilities.
        stay = p[np.arange(len(p)), current].copy()
        h = 1 - stay
        adjusted = h * (1 - threshold) / np.maximum(h * (1 - threshold) + (1 - h) * threshold, 1e-12)
        p *= np.divide(adjusted, h, out=np.zeros_like(h), where=h > 0)[:, None]
        p[np.arange(len(p)), current] = 1 - adjusted
        candidate_scores = scores[:, :, None] + np.log(np.maximum(p.reshape(n, b, 5), 1e-30))
        next_width = min(width, b * 5)
        order = np.argsort(-candidate_scores.reshape(n, -1), axis=1, kind='stable')[:, :next_width]
        parent, action = order // 5, order % 5
        ix = np.arange(n)[:, None]
        scores = np.take_along_axis(candidate_scores.reshape(n, -1), order, axis=1)
        anchors = anchors[ix, parent].copy()
        old_steps = steps[ix, parent]
        changed = anchors[:, :, 0].astype(int) != action
        anchors[:, :, 3] = np.where(changed, anchors[:, :, 2], anchors[:, :, 3])
        anchors[:, :, 2] = np.where(changed, anchors[:, :, 0], anchors[:, :, 2])
        anchors[:, :, 0] = action
        anchors[:, :, 1] = np.where(changed, 0, anchors[:, :, 1])
        steps = np.where(changed, 1, old_steps + 1)
        history = np.concatenate((history[ix, parent], action[:, :, None]), axis=2)
    return history, scores


def sequence_metrics(current, truth, pred, tolerance=1):
    actual_events = np.diff(np.column_stack((current, truth)), axis=1) != 0
    predicted_events = np.diff(np.column_stack((current, pred)), axis=1) != 0
    correct = pred == truth
    near = actual_events.copy()
    near[:, 1:] |= actual_events[:, :-1]
    near[:, :-1] |= actual_events[:, 1:]
    changed = actual_events.any(axis=1)
    matched = missed = extra = wrong_destination = 0
    timing = []
    longest = []
    for a, p, actual, prediction, ok in zip(actual_events, predicted_events, truth, pred, correct):
        actual_times = np.flatnonzero(a)
        available = list(np.flatnonzero(p))
        # Chronological one-to-one matching, closest within +/-1 frame;
        # destinations are evaluated after timing match (no favorable relabeling).
        for t in actual_times:
            candidates = [u for u in available if abs(u-t) <= tolerance]
            if not candidates:
                missed += 1
                continue
            u = min(candidates, key=lambda u: (abs(u-t), u))
            available.remove(u)
            matched += 1
            timing.append(abs(u-t))
            wrong_destination += int(actual[t] != prediction[u])
        extra += len(available)
        run = maximum = 0
        for value in ok:
            run = 0 if value else run + 1
            maximum = max(maximum, run)
        longest.append(maximum)
    total = matched + missed
    return dict(paths=len(truth), frame_accuracy=float(correct.mean()),
                endpoint_accuracy=float(correct[:, -1].mean()),
                full_path_accuracy=float(correct.all(axis=1).mean()),
                change_path_accuracy=float(correct[changed].mean()) if changed.any() else None,
                near_change_accuracy=float(correct[near].mean()) if near.any() else None,
                missed_event_rate=missed/total if total else None,
                false_alarm_path_rate=float(predicted_events[~changed].any(axis=1).mean()) if (~changed).any() else None,
                matched_destination_accuracy=1-wrong_destination/matched if matched else None,
                timing_mae_frames=float(np.mean(timing)) if timing else None,
                mean_longest_error_run=float(np.mean(longest)),
                actual_events=total, matched_events=matched, missed_events=missed,
                unmatched_predicted_events=extra, wrong_destinations=wrong_destination,
                changed_paths=int(changed.sum()))


def measure(call, samples):
    for j in range(20):
        call(j % samples)
    times = []
    for j in range(samples):
        t = perf_counter_ns()
        call(j)
        times.append((perf_counter_ns()-t)/1e6)
    return dict(samples=samples, mean_ms=float(np.mean(times)),
                p50_ms=float(np.median(times)), p95_ms=float(np.percentile(times, 95)))


def choose(records, limit):
    allowed = [r for r in records if r['false_alarm_path_rate'] is not None and r['false_alarm_path_rate'] <= limit]
    if allowed:
        return max(allowed, key=lambda r: (r['near_change_accuracy'] or 0, r['frame_accuracy']))
    return min(records, key=lambda r: (r['false_alarm_path_rate'] if r['false_alarm_path_rate'] is not None else 1,
                                       -(r['near_change_accuracy'] or 0)))


def run(logs, output, split_path):
    out = Path(output)
    out.mkdir(parents=True, exist_ok=True)
    data = load_match_logs(logs)
    f = build_feature_frame(data.frames, data.events)
    split = json.loads(Path(split_path).read_text())
    subsets = apply_session_split(f, split).to_numpy()
    targets = event_targets(f)
    base = features_for_events(f, targets)
    train = np.flatnonzero(subsets == 'train')
    current = targets.current.to_numpy()
    print(f'Loaded {f.match_id.nunique()} rounds, {f.session_id.nunique()} sessions, {len(f)} rows', flush=True)
    fep = fit_active_inference_model(f, split)
    beliefs = infer_frame_beliefs(f, fep)
    fep.save(out / 'fep.npz')
    inputs = {'event': base, 'event_fep': np.column_stack((base, beliefs)).astype(np.float32)}
    models = {}
    for variant, x in inputs.items():
        for weight in (1, 3):
            print(f'Train {variant} weight={weight}', flush=True)
            models[variant, weight] = EventModel().fit(x, targets, train, weight)
            joblib.dump(models[variant, weight], out / f'{variant}_weight{weight}.joblib')
    # Retrain the existing direct-XGBoost method on identical train sessions for
    # each offset: endpoint models alone cannot supply intermediate actions.
    direct_x = f[FEATURE_COLUMNS].to_numpy(np.float32)
    direct = []
    for h in range(1, 13):
        p, _ = paths_for(f, split, 'train', h)
        model = XGBClassifier(n_estimators=80, max_depth=4, learning_rate=.08,
                              tree_method='hist', n_jobs=2, random_state=42)
        y = current[p[:, -1]]
        labels = np.unique(y)
        model.fit(direct_x[p[:, 0]], np.searchsorted(labels, y))
        direct.append((model, labels))
    joblib.dump(direct, out / 'direct_offset_models.joblib')
    report = dict(data=dict(rounds=int(f.match_id.nunique()), sessions=int(f.session_id.nunique()),
                           rows=len(f), source_sha256=hashlib.sha256(Path(logs).read_bytes()).hexdigest(),
                           subsets={s: dict(rounds=int(f.loc[subsets == s, 'match_id'].nunique()),
                                           sessions=int(f.loc[subsets == s, 'session_id'].nunique()),
                                           rows=int((subsets == s).sum())) for s in ('train', 'validation', 'test')}),
                  split=split, horizons={}, validation=[], operating_points=[], timing={},
                  methodology=dict(context_policy='Physical/opponent context and FEP belief frozen at observed start; only predicted action state changes. No future logged states are inputs.',
                                   event_tolerance_frames=1, thresholds=[.25, .5, .75], exit_weights=[1, 3],
                                   widths=[1, 3], false_alarm_limits=[.05, .10, .20],
                                   selection='validation only: highest near-change accuracy under no-change-path false-alarm limit; default .10; fallback lowest false-alarm rate',
                                   excluded='gaps and paths containing forced/terminal/unknown classes',
                                   caveat='Existing test informed the original plan. No new-session validation or counterfactual HP/game-state accuracy is claimed.',
                                   feature_names=DYNAMIC+CONTEXT, fep_features='three causally filtered start beliefs appended',
                                   timing='warm per-start single-row calls; excludes loading and initial feature/FEP extraction; includes beam maintenance for rollout timing',
                                   platform=platform.platform()), per_session=[])
    for name, h in HORIZONS.items():
        print(f'Evaluate {name}', flush=True)
        val, val_excluded = paths_for(f, split, 'validation', h)
        test, test_excluded = paths_for(f, split, 'test', h)
        vi, ti = val[:, 0], test[:, 0]
        truth_val, truth_test = current[val[:, 1:]], current[test[:, 1:]]
        result = dict(validation_paths=len(val), test_paths=len(test),
                      validation_excluded=val_excluded, test_excluded=test_excluded, models={})
        predictions = {'persistence': np.repeat(current[ti, None], h, axis=1),
                       'direct_xgboost': np.column_stack([labels[model.predict(direct_x[ti]).astype(int)] for model, labels in direct[:h]])}
        saved = dict(path_indices=test, truth=truth_test, current=current[ti], session_id=f.session_id.to_numpy()[ti])
        for variant, x in inputs.items():
            for width in (1, 3):
                records = []
                for weight in (1, 3):
                    model = models[variant, weight]
                    for threshold in (.25, .5, .75):
                        candidates, _ = rollout(model, x[vi], h, width, threshold)
                        records.append(dict(horizon=name, variant=variant, width=width, weight=weight,
                                            threshold=threshold, **sequence_metrics(current[vi], truth_val, candidates[:, 0])))
                report['validation'].extend(records)
                for limit in (.05, .10, .20):
                    chosen = choose(records, limit)
                    report['operating_points'].append(dict(horizon=name, variant=variant, width=width,
                                                          false_alarm_limit=limit, validation_choice=chosen,
                                                          constraint_met=(chosen['false_alarm_path_rate'] or 0) <= limit))
                chosen = choose(records, .10)
                model = models[variant, chosen['weight']]
                candidates, scores = rollout(model, x[ti], h, width, chosen['threshold'])
                key = f'{variant}_beam{width}'
                predictions[key] = candidates[:, 0]
                saved[key+'_candidates'] = candidates
                saved[key+'_scores'] = scores
                result['models'][key] = dict(selected_weight=chosen['weight'], threshold=chosen['threshold'],
                    oracle_full_path_coverage=float((candidates == truth_test[:, None, :]).all(axis=2).any(axis=1).mean()))
                count = min(100, len(ti))
                ids = ti[np.linspace(0, len(ti)-1, count, dtype=int)]
                timing = measure(lambda j: rollout(model, x[ids[j]:ids[j]+1], h, width, chosen['threshold']), count)
                timing['amortized_ms_per_predicted_frame'] = timing['mean_ms']/h
                result['models'][key]['rollout_time'] = timing
                if name == '50ms':
                    report['timing'][key+'_next_frame'] = measure(
                        lambda j: rollout(model, x[ids[j]:ids[j]+1], 1, width, chosen['threshold']), count)
        for key, pred in predictions.items():
            result['models'].setdefault(key, {}).update(sequence_metrics(current[ti], truth_test, pred))
            saved[key] = pred
            for session in sorted(f.session_id.iloc[ti].unique()):
                mask = f.session_id.to_numpy()[ti] == session
                report['per_session'].append(dict(horizon=name, model=key, session_id=session,
                    **sequence_metrics(current[ti][mask], truth_test[mask], pred[mask])))
        report['horizons'][name] = result
        np.savez_compressed(out / f'predictions_{name}.npz', **saved)
        (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    ti = paths_for(f, split, 'test', 1)[0][:, 0]
    count = min(500, len(ti))
    ids = ti[np.linspace(0, len(ti)-1, count, dtype=int)]
    model = direct[0][0]
    report['timing']['direct_xgboost_next_frame'] = measure(lambda j: model.predict(direct_x[ids[j]:ids[j]+1]), count)
    # Raw model timings additionally use 500 independent single-frame calls.
    for variant, x in inputs.items():
        weight = report['horizons']['50ms']['models'][variant+'_beam1']['selected_weight']
        model = models[variant, weight]
        report['timing'][variant+'_raw_next_frame'] = measure(lambda j: model.predict(x[ids[j]:ids[j]+1], current[ids[j]:ids[j]+1]), count)
    (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    write_report(report, out)
    print(json.dumps(report['timing'], indent=2), flush=True)
    return report


def write_report(report, out):
    for horizon, result in report['horizons'].items():
        selected = choose([r for r in report['validation'] if r['horizon'] == horizon], .10)
        result['validation_selected_model'] = f"{selected['variant']}_beam{selected['width']}"
        result['validation_constraint_met'] = (selected['false_alarm_path_rate'] or 0) <= .10
    lines = ['# 200ラウンド・オフライン行動列予測比較', '',
             f"200ラウンド、{report['data']['sessions']}セッション、{report['data']['rows']}行。既存splitを再利用。", '',
             '## データ分割', '', '|区分|ラウンド|セッション|行|', '|---|---:|---:|---:|']
    for name, d in report['data']['subsets'].items():
        lines.append(f"|{name}|{d['rounds']}|{d['sessions']}|{d['rows']}|")
    lines += ['', '## 比較結果', '',
              '各方式は同じ開始点・区間。変化は区間内の全イベントを対象とし、元の行動へ戻る経路も含む。強制行動・欠落を含む区間は全方式共通で除外。', '',
              '|遅延|方式|フレーム正答率|変化周辺正答率|全経路一致|見逃し率|誤警報率|', '|---|---|---:|---:|---:|---:|---:|']
    def pct(value):
        return 'N/A' if value is None else f'{100*value:.2f}%'
    for horizon, r in report['horizons'].items():
        for name, m in r['models'].items():
            values = [pct(m[k]) for k in ('frame_accuracy', 'near_change_accuracy', 'full_path_accuracy', 'missed_event_rate', 'false_alarm_path_rate')]
            lines.append('|'+ '|'.join([horizon, name]+values)+'|')
    lines += ['', '## 次フレームの予測時間', '', '|方式|件数|平均ms|p95 ms|', '|---|---:|---:|---:|']
    for name, t in report['timing'].items():
        lines.append(f"|{name}|{t['samples']}|{t['mean_ms']:.4f}|{t['p95_ms']:.4f}|")
    lines += ['', '## Validationによる候補選択と失敗分析', '']
    for horizon, r in report['horizons'].items():
        name = r['validation_selected_model']
        m = r['models'][name]
        direct = r['models']['direct_xgboost']
        delta = 100 * (m['near_change_accuracy'] - direct['near_change_accuracy'])
        sessions = [s['near_change_accuracy'] for s in report['per_session']
                    if s['horizon'] == horizon and s['model'] == name and s['near_change_accuracy'] is not None]
        lines += [f"- {horizon}: validation選択は **{name}**（重み{m['selected_weight']}、閾値{m['threshold']}）。"
                  f"testの変化周辺正答率は直接XGBoost比 {delta:+.2f}ポイント。"
                  f"見逃し{m['missed_events']}/{m['actual_events']}、時刻照合後の変化先誤り{m['wrong_destinations']}/{m['matched_events']}、"
                  f"余分な切替{m['unmatched_predicted_events']}。"
                  f"セッション別変化周辺正答率は{min(sessions)*100:.1f}～{max(sessions)*100:.1f}%。"
                  f"validationの誤警報10%条件達成: {r['validation_constraint_met']}。"]
    lines += ['', 'ウォームアップ20回後、単件呼び出しを計測。rawはハザード＋変化先推論、beamは候補選択も含む。初期特徴量・FEP信念生成とI/Oは含まない。区間全体の時間はreport.jsonに別記。', '',
              '## 評価条件と読み方', '',
              '- ハザードは切り替わるまでのkを条件にする。切り替わり後は予測クラス・履歴を更新して再予測。末尾・欠落・強制状態は打切り。',
              '- FEPはtrainだけで学習し、開始時までのフィルタ信念を追加。位置・HP・相手状態・FEP信念は開始時固定。実ゲームを進めた評価ではない。',
              '- beam3はモデルスコア最大の1本を採用。候補内正解経路の包含率（oracle）は別項目であり、採用精度と混同しない。',
              '- 閾値はハザードのオッズ補正。validationで重み1/3・閾値0.25/0.5/0.75を比較。変化しない区間の誤警報率10%以下で変化周辺正答率最大を選択。5/20%の許容水準もvalidation結果を保存。',
              '- イベントは時刻順に±1フレーム以内の最も近い予測と1対1照合。変化先正答率は時刻照合後に集計。変化周辺は正解切替フレームと前後1フレーム。',
              '- 見逃し・余分な切替・変化先誤り・時刻MAE・最長連続誤り・セッション別の指標はreport.json。重複区間のイベント数は独立イベント数ではない。',
              '- direct_xgboostは既存方式を同じtrainで各オフセット1～12について再学習し、途中予測を連結。既存保存モデルの過去スコアを転載したものではない。',
              '- ゲーム実行は今回の対象外。反実仮想のHP/ガード/位置誤差は算出しない。既存testに基づく計画の追試で、新規セッションに対する汎化保証ではない。', '',
              '再実行: `python -m action_prediction.sequence_experiments --logs ../CombatGame/MatchLogs.zip --split artifacts/split_assignment.json --output artifacts/sequence_offline`', '']
    (out / 'experiment_report.md').write_text('\n'.join(lines), encoding='utf-8')
    (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--logs', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--split', required=True)
    a = parser.parse_args()
    run(a.logs, a.output, a.split)


if __name__ == '__main__':
    main()
