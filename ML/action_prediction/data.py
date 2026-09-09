"""Read MatchLogger output from an extracted directory or ZIP archive."""

from __future__ import annotations

import io
import zipfile
from dataclasses import dataclass
from pathlib import Path

import pandas as pd


@dataclass(frozen=True)
class MatchLogs:
    frames: pd.DataFrame
    events: pd.DataFrame
    matches: pd.DataFrame


def _read_csvs_from_zip(path: Path, filename: str) -> pd.DataFrame:
    frames: list[pd.DataFrame] = []
    with zipfile.ZipFile(path) as archive:
        names = sorted(
            name
            for name in archive.namelist()
            if name.startswith("MatchLogs/") and name.endswith(f"/{filename}")
        )
        for name in names:
            with archive.open(name) as stream:
                raw = stream.read()
            if raw.strip():
                frame = pd.read_csv(io.BytesIO(raw), encoding="utf-8-sig")
                frame["source_match_dir"] = Path(name).parent.name
                frames.append(frame)
    return pd.concat(frames, ignore_index=True, sort=False) if frames else pd.DataFrame()


def _read_csvs_from_directory(path: Path, filename: str) -> pd.DataFrame:
    frames: list[pd.DataFrame] = []
    for csv_path in sorted(path.glob(f"*/{filename}")):
        if csv_path.stat().st_size == 0:
            continue
        frame = pd.read_csv(csv_path, encoding="utf-8-sig")
        frame["source_match_dir"] = csv_path.parent.name
        frames.append(frame)
    return pd.concat(frames, ignore_index=True, sort=False) if frames else pd.DataFrame()


def load_match_logs(path: str | Path) -> MatchLogs:
    """Load frames/events/matches and normalize canonical frame rows."""
    source = Path(path)
    if not source.exists():
        raise FileNotFoundError(f"Match log source does not exist: {source}")
    reader = _read_csvs_from_zip if source.is_file() else _read_csvs_from_directory
    frames = reader(source, "frames.csv")
    events = reader(source, "events.csv")
    matches = reader(source, "matches.csv")
    if frames.empty:
        raise ValueError(f"No frames.csv files found below {source}")

    frames = _canonicalize_frames(frames)
    events = _canonicalize_events(events)
    frames = _attach_session_ids(frames, matches)
    return MatchLogs(frames=frames, events=events, matches=matches)


def _as_bool(series: pd.Series) -> pd.Series:
    if pd.api.types.is_bool_dtype(series):
        return series.fillna(False)
    return series.astype(str).str.strip().str.lower().isin({"true", "1", "yes"})


def _canonicalize_frames(frames: pd.DataFrame) -> pd.DataFrame:
    required = {"match_id", "frame", "player_id", "p1_action_id", "p2_action_id"}
    missing = required.difference(frames.columns)
    if missing:
        raise ValueError(f"frames.csv is missing required columns: {sorted(missing)}")

    result = frames.copy()
    if "is_canonical" in result.columns:
        canonical = _as_bool(result["is_canonical"])
        result = result[canonical | result["is_canonical"].isna()].copy()
    elif "simulation_pass" in result.columns:
        keys = ["match_id", "player_id", "frame"]
        max_pass = pd.to_numeric(result["simulation_pass"], errors="coerce").groupby(
            [result[key] for key in keys]
        ).transform("max")
        result = result[pd.to_numeric(result["simulation_pass"], errors="coerce") == max_pass]

    result["frame"] = pd.to_numeric(result["frame"], errors="raise").astype(int)
    result["player_id"] = pd.to_numeric(result["player_id"], errors="raise").astype(int)
    result = result.sort_values(["match_id", "player_id", "frame"])
    return result.drop_duplicates(["match_id", "player_id", "frame"], keep="last").reset_index(drop=True)


def _canonicalize_events(events: pd.DataFrame) -> pd.DataFrame:
    if events.empty:
        return events
    result = events.copy()
    if "is_canonical" in result.columns:
        result = result[_as_bool(result["is_canonical"]) | result["is_canonical"].isna()]
    if "event_frame" in result.columns:
        result["event_frame"] = pd.to_numeric(result["event_frame"], errors="coerce").astype("Int64")
    return result.reset_index(drop=True)


def _attach_session_ids(frames: pd.DataFrame, matches: pd.DataFrame) -> pd.DataFrame:
    result = frames.copy()
    if not matches.empty and {"match_id", "session_id"}.issubset(matches.columns):
        lookup = (
            matches[["match_id", "session_id"]]
            .dropna(subset=["match_id"])
            .drop_duplicates("match_id", keep="first")
        )
        result = result.merge(lookup, on="match_id", how="left")
    if "session_id" not in result.columns:
        result["session_id"] = result["match_id"]
    result["session_id"] = result["session_id"].fillna(result["match_id"]).astype(str)
    return result

