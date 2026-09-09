"""Majority, persistence, and first-order Markov baselines."""

from __future__ import annotations

import numpy as np

from .constants import ACTION_CLASSES


def class_prior(y_train: np.ndarray, smoothing: float = 1.0) -> np.ndarray:
    counts = np.bincount(y_train.astype(int), minlength=len(ACTION_CLASSES)).astype(float) + smoothing
    return counts / counts.sum()


def majority_probabilities(y_train: np.ndarray, row_count: int) -> np.ndarray:
    prior = class_prior(y_train)
    winner = int(prior.argmax())
    probabilities = np.zeros((row_count, len(ACTION_CLASSES)), dtype=float)
    probabilities[:, winner] = 1.0
    return probabilities


def persistence_probabilities(current_classes: np.ndarray, y_train: np.ndarray) -> np.ndarray:
    prior = class_prior(y_train)
    probabilities = np.tile(prior, (len(current_classes), 1))
    valid = (current_classes >= 0) & (current_classes < len(ACTION_CLASSES))
    probabilities[valid] = 0.0
    probabilities[np.flatnonzero(valid), current_classes[valid].astype(int)] = 1.0
    return probabilities


def fit_markov(current_classes: np.ndarray, targets: np.ndarray, smoothing: float = 1.0) -> np.ndarray:
    transitions = np.full((len(ACTION_CLASSES), len(ACTION_CLASSES)), smoothing, dtype=float)
    valid = (current_classes >= 0) & (current_classes < len(ACTION_CLASSES))
    np.add.at(transitions, (current_classes[valid].astype(int), targets[valid].astype(int)), 1.0)
    return transitions / transitions.sum(axis=1, keepdims=True)


def markov_probabilities(
    transition_matrix: np.ndarray,
    current_classes: np.ndarray,
    y_train: np.ndarray,
) -> np.ndarray:
    probabilities = np.tile(class_prior(y_train), (len(current_classes), 1))
    valid = (current_classes >= 0) & (current_classes < len(ACTION_CLASSES))
    probabilities[valid] = transition_matrix[current_classes[valid].astype(int)]
    return probabilities

