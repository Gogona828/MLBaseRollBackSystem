"""Fixed-five-class evaluation helpers."""

from __future__ import annotations

import numpy as np
from sklearn.metrics import accuracy_score, confusion_matrix, f1_score, log_loss, top_k_accuracy_score

from .constants import ACTION_CLASSES


def _accuracy_for_mask(
    y_true: np.ndarray,
    predictions: np.ndarray,
    mask: np.ndarray,
) -> float | None:
    """Return accuracy for a subset, or None when the subset has no samples."""
    if not np.any(mask):
        return None
    return float(accuracy_score(y_true[mask], predictions[mask]))


def evaluate_probabilities(
    y_true: np.ndarray,
    probabilities: np.ndarray,
    current_actions: np.ndarray | None = None,
) -> dict[str, object]:
    """Evaluate predictions, optionally separating changed and unchanged actions.

    An action is considered changed when its class at prediction time differs
    from the ground-truth class at the prediction horizon.
    """
    labels = np.arange(len(ACTION_CLASSES))
    y_true = np.asarray(y_true, dtype=int)
    probabilities = np.asarray(probabilities, dtype=float)
    if probabilities.shape != (len(y_true), len(ACTION_CLASSES)):
        raise ValueError(
            "probabilities must have shape "
            f"({len(y_true)}, {len(ACTION_CLASSES)}), got {probabilities.shape}"
        )
    probabilities = np.clip(probabilities, 1e-15, 1.0)
    probabilities /= probabilities.sum(axis=1, keepdims=True)
    predictions = probabilities.argmax(axis=1)
    metrics: dict[str, object] = {
        "accuracy": float(accuracy_score(y_true, predictions)),
        "top2_accuracy": float(top_k_accuracy_score(y_true, probabilities, k=2, labels=labels)),
        "macro_f1": float(f1_score(y_true, predictions, labels=labels, average="macro", zero_division=0)),
        "log_loss": float(log_loss(y_true, probabilities, labels=labels)),
        "confusion_matrix": confusion_matrix(y_true, predictions, labels=labels).tolist(),
    }
    if current_actions is not None:
        current_actions = np.asarray(current_actions, dtype=int)
        if current_actions.shape != y_true.shape:
            raise ValueError(
                f"current_actions must have shape {y_true.shape}, got {current_actions.shape}"
            )
        changed_mask = current_actions != y_true
        unchanged_mask = ~changed_mask
        metrics.update(
            {
                "changed_action_samples": int(changed_mask.sum()),
                "changed_action_accuracy": _accuracy_for_mask(y_true, predictions, changed_mask),
                "unchanged_action_samples": int(unchanged_mask.sum()),
                "unchanged_action_accuracy": _accuracy_for_mask(y_true, predictions, unchanged_mask),
            }
        )
    return metrics
