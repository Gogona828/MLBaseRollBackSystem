"""Leakage-safe current-state and past-history feature generation."""

from __future__ import annotations

from collections.abc import Iterable

import numpy as np
import pandas as pd

from .constants import action_id_to_class, action_id_to_state_group

GROUP_KEYS = ["match_id", "player_id"]

CURRENT_FEATURES = [
    "fixed_delta_time",
    "input_bits",
    "input_left",
    "input_right",
    "input_attack",
    "input_down_bits",
    "input_up_bits",
    "self_position_x",
    "self_position_y",
    "self_velocity_x",
    "self_is_face_right",
    "self_vital_health",
    "self_guard_health",
    "self_is_dead",
    "self_action_id",
    "self_action_group",
    "self_action_frame",
    "self_hitstun_frame",
    "self_is_in_hitstun",
    "self_is_cornered",
    "opponent_position_x",
    "opponent_position_y",
    "opponent_velocity_x",
    "opponent_is_face_right",
    "opponent_vital_health",
    "opponent_guard_health",
    "opponent_is_dead",
    "opponent_action_id",
    "opponent_action_group",
    "opponent_action_frame",
    "opponent_hitstun_frame",
    "opponent_is_in_hitstun",
    "opponent_is_cornered",
    "signed_distance_to_opponent",
    "absolute_distance",
    "opponent_relative_velocity",
]

HISTORY_FEATURES = [
    "distance_3_frames_ago",
    "distance_6_frames_ago",
    "distance_12_frames_ago",
    "relative_velocity_mean_12",
    "distance_change_12",
    "previous_action_id_1",
    "previous_action_id_2",
    "attack_frames_last_30",
    "guard_frames_last_30",
    "attack_hits_last_30",
    "guards_succeeded_last_30",
    "guard_breaks_last_30",
]

FEATURE_COLUMNS = CURRENT_FEATURES + HISTORY_FEATURES

BOOL_COLUMNS = {
    "input_left",
    "input_right",
    "input_attack",
    "self_is_face_right",
    "self_is_dead",
    "self_is_in_hitstun",
    "self_is_cornered",
    "opponent_is_face_right",
    "opponent_is_dead",
    "opponent_is_in_hitstun",
    "opponent_is_cornered",
}


def _numeric(frame: pd.DataFrame, name: str, default: float = 0.0) -> pd.Series:
    if name not in frame.columns:
        return pd.Series(default, index=frame.index, dtype=float)
    if name in BOOL_COLUMNS or name.startswith("p1_is_") or name.startswith("p2_is_") or name.startswith("input_") and name not in {"input_bits", "input_down_bits", "input_up_bits"}:
        raw = frame[name]
        if pd.api.types.is_bool_dtype(raw):
            return raw.fillna(False).astype(float)
        return raw.astype(str).str.lower().isin({"true", "1", "yes"}).astype(float)
    return pd.to_numeric(frame[name], errors="coerce").fillna(default)


def _select_player_value(frame: pd.DataFrame, suffix: str, own: bool) -> pd.Series:
    is_p1 = frame["player_id"].eq(1)
    p1 = _numeric(frame, f"p1_{suffix}")
    p2 = _numeric(frame, f"p2_{suffix}")
    return pd.Series(np.where(is_p1 if own else ~is_p1, p1, p2), index=frame.index)


def _event_flags(frame: pd.DataFrame, events: pd.DataFrame) -> pd.DataFrame:
    result = pd.DataFrame(index=frame.index)
    event_names = ("attack_hit", "attack_guarded", "guard_break")
    for name in event_names:
        result[name] = 0.0
    if events.empty or not {"match_id", "event_frame", "event_type"}.issubset(events.columns):
        return result
    counts = (
        events[events["event_type"].isin(event_names)]
        .groupby(["match_id", "event_frame", "event_type"])
        .size()
        .unstack(fill_value=0)
        .reset_index()
        .rename(columns={"event_frame": "frame"})
    )
    lookup = frame[["match_id", "frame"]].merge(counts, on=["match_id", "frame"], how="left")
    for name in event_names:
        if name in lookup:
            result[name] = pd.to_numeric(lookup[name], errors="coerce").fillna(0).to_numpy()
    return result


def _previous_distinct(values: Iterable[float]) -> tuple[np.ndarray, np.ndarray]:
    values_array = np.asarray(list(values), dtype=float)
    previous_one = np.full(len(values_array), -1.0)
    previous_two = np.full(len(values_array), -1.0)
    current: float | None = None
    last_distinct = -1.0
    second_last_distinct = -1.0
    for index, value in enumerate(values_array):
        if current is None:
            current = value
        elif value != current:
            second_last_distinct = last_distinct
            last_distinct = current
            current = value
        previous_one[index] = last_distinct
        previous_two[index] = second_last_distinct
    return previous_one, previous_two


def build_feature_frame(frames: pd.DataFrame, events: pd.DataFrame | None = None) -> pd.DataFrame:
    """Create features using only information observable at or before each row's frame."""
    source = frames.sort_values(GROUP_KEYS + ["frame"]).reset_index(drop=True).copy()
    result = source[["match_id", "session_id", "player_id", "frame"]].copy()

    for name in ("fixed_delta_time", "input_bits", "input_left", "input_right", "input_attack", "input_down_bits", "input_up_bits"):
        result[name] = _numeric(source, name)

    suffixes = (
        "position_x", "position_y", "velocity_x", "is_face_right", "vital_health",
        "guard_health", "is_dead", "action_id", "action_frame", "hitstun_frame",
        "is_in_hitstun", "is_cornered",
    )
    for suffix in suffixes:
        result[f"self_{suffix}"] = _select_player_value(source, suffix, own=True)
        result[f"opponent_{suffix}"] = _select_player_value(source, suffix, own=False)

    result["self_action_group"] = result["self_action_id"].map(action_id_to_state_group)
    result["opponent_action_group"] = result["opponent_action_id"].map(action_id_to_state_group)
    result["signed_distance_to_opponent"] = np.where(
        source["player_id"].eq(1), _numeric(source, "distance_x"), -_numeric(source, "distance_x")
    )
    result["absolute_distance"] = _numeric(source, "absolute_distance")
    result["opponent_relative_velocity"] = np.where(
        source["player_id"].eq(1), _numeric(source, "relative_velocity"), -_numeric(source, "relative_velocity")
    )

    event_flags = _event_flags(source, events if events is not None else pd.DataFrame())
    grouped = result.groupby(GROUP_KEYS, sort=False, group_keys=False)
    for lag in (3, 6, 12):
        shifted_distance = grouped["absolute_distance"].shift(lag)
        shifted_frame = grouped["frame"].shift(lag)
        result[f"distance_{lag}_frames_ago"] = shifted_distance.where(result["frame"] - shifted_frame == lag)
    result["relative_velocity_mean_12"] = grouped["opponent_relative_velocity"].transform(
        lambda values: values.rolling(12, min_periods=1).mean()
    )
    result["distance_change_12"] = result["absolute_distance"] - result["distance_12_frames_ago"]

    result["previous_action_id_1"] = -1.0
    result["previous_action_id_2"] = -1.0
    for _, indices in result.groupby(GROUP_KEYS, sort=False).groups.items():
        previous_one, previous_two = _previous_distinct(result.loc[indices, "self_action_id"])
        result.loc[indices, "previous_action_id_1"] = previous_one
        result.loc[indices, "previous_action_id_2"] = previous_two

    current_class = result["self_action_id"].map(action_id_to_class)
    result["attack_frames_last_30"] = current_class.eq(3).astype(float)
    result["guard_frames_last_30"] = current_class.eq(4).astype(float)
    result["attack_hits_last_30"] = event_flags["attack_hit"].to_numpy()
    result["guards_succeeded_last_30"] = event_flags["attack_guarded"].to_numpy()
    result["guard_breaks_last_30"] = event_flags["guard_break"].to_numpy()
    grouped = result.groupby(GROUP_KEYS, sort=False, group_keys=False)
    for name in (
        "attack_frames_last_30", "guard_frames_last_30", "attack_hits_last_30",
        "guards_succeeded_last_30", "guard_breaks_last_30",
    ):
        result[name] = grouped[name].transform(lambda values: values.rolling(30, min_periods=1).sum())

    # Explicitly exclude p1/p2_buffer_action_id and every future-derived value.
    return result[["match_id", "session_id", "player_id", "frame"] + FEATURE_COLUMNS]


def build_supervised_dataset(feature_frame: pd.DataFrame, horizon_frames: int) -> pd.DataFrame:
    """Attach an exact t+h voluntary action label; forced/terminal targets are removed."""
    if horizon_frames <= 0:
        raise ValueError("horizon_frames must be positive")
    result = feature_frame.sort_values(GROUP_KEYS + ["frame"]).copy()
    grouped = result.groupby(GROUP_KEYS, sort=False)
    future_frame = grouped["frame"].shift(-horizon_frames)
    future_action_id = grouped["self_action_id"].shift(-horizon_frames)
    exact = future_frame - result["frame"] == horizon_frames
    result["target_action_id"] = future_action_id.where(exact)
    result["target_class"] = result["target_action_id"].map(action_id_to_class)
    return result[result["target_class"].ge(0)].assign(target_class=lambda x: x["target_class"].astype(int))

