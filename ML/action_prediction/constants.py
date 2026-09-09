"""Shared labels and horizon definitions."""

from __future__ import annotations

from typing import Final

ACTION_CLASSES: Final[tuple[str, ...]] = (
    "Wait",
    "Approach",
    "Retreat",
    "Attack",
    "Guard",
)

ACTION_ID_TO_CLASS: Final[dict[int, int]] = {
    0: 0,
    1: 1,
    10: 1,
    2: 2,
    11: 2,
    100: 3,
    105: 3,
    110: 3,
    115: 3,
    301: 4,
    305: 4,
    306: 4,
    350: 4,
}

FORCED_ACTION_IDS: Final[frozenset[int]] = frozenset({200, 310})
TERMINAL_ACTION_IDS: Final[frozenset[int]] = frozenset({500, 510})

# label -> (milliseconds, frames at 60 FPS)
HORIZONS: Final[dict[str, tuple[int, int]]] = {
    "50ms": (50, 3),
    "100ms": (100, 6),
    "200ms": (200, 12),
    "300ms": (300, 18),
    "500ms": (500, 30),
    "1s": (1000, 60),
    "2s": (2000, 120),
    "3s": (3000, 180),
}


def action_id_to_class(action_id: int | float | str | None) -> int:
    """Map a Unity action ID to 0..4, returning -1 for forced/unknown states."""
    if action_id is None:
        return -1
    try:
        return ACTION_ID_TO_CLASS.get(int(action_id), -1)
    except (TypeError, ValueError):
        return -1


def action_id_to_state_group(action_id: int | float | str | None) -> int:
    """Map input state to 5 voluntary classes plus forced/terminal/unknown groups."""
    mapped = action_id_to_class(action_id)
    if mapped >= 0:
        return mapped
    try:
        value = int(action_id)  # type: ignore[arg-type]
    except (TypeError, ValueError):
        return 7
    if value in FORCED_ACTION_IDS:
        return 5
    if value in TERMINAL_ACTION_IDS:
        return 6
    return 7

