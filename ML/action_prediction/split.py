"""Deterministic session-level train/validation/test splitting."""

from __future__ import annotations

import numpy as np
import pandas as pd


def make_session_split(
    session_ids: pd.Series,
    seed: int = 42,
    train_fraction: float = 0.70,
    validation_fraction: float = 0.15,
) -> dict[str, str]:
    unique = np.array(sorted(session_ids.dropna().astype(str).unique()))
    if len(unique) < 3:
        raise ValueError("At least three sessions are required for train/validation/test splitting")
    rng = np.random.default_rng(seed)
    rng.shuffle(unique)
    train_end = max(1, int(round(len(unique) * train_fraction)))
    validation_count = max(1, int(round(len(unique) * validation_fraction)))
    validation_end = min(len(unique) - 1, train_end + validation_count)
    train_end = min(train_end, validation_end - 1)
    return {
        session: "train" if index < train_end else "validation" if index < validation_end else "test"
        for index, session in enumerate(unique)
    }


def apply_session_split(frame: pd.DataFrame, assignment: dict[str, str]) -> pd.Series:
    split = frame["session_id"].astype(str).map(assignment)
    if split.isna().any():
        missing = sorted(frame.loc[split.isna(), "session_id"].astype(str).unique())
        raise ValueError(f"No split assignment for sessions: {missing}")
    return split

