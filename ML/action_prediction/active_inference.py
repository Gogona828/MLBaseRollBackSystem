"""Discrete active-inference model adapted from the reconstructed fep-ai snapshot.

The snapshot defines three latent tactical states, five observations, sequential
Bayesian belief updating, and policy probabilities from habit priors and expected
free energy.  This module preserves that structure while fitting the observation
and transition matrices on training MatchLogs only.  The toy ``probe`` policy is
mapped to the observable Wait class for the five-class comparison.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd

from .constants import ACTION_CLASSES, action_id_to_class

LATENT_STATES = ("aggressive", "defensive", "approaching")

# Snapshot observation order was attack, guard, approach, retreat, wait.
# Rows below are reordered to the shared Wait, Approach, Retreat, Attack, Guard order.
TOY_OBSERVATION_LIKELIHOOD = np.array(
    [
        [0.15, 0.15, 0.20],  # Wait
        [0.25, 0.05, 0.45],  # Approach
        [0.05, 0.25, 0.10],  # Retreat
        [0.45, 0.10, 0.15],  # Attack
        [0.10, 0.45, 0.10],  # Guard
    ],
    dtype=float,
)

TOY_TRANSITION = np.array(
    [
        [0.85, 0.075, 0.075],
        [0.075, 0.85, 0.075],
        [0.075, 0.075, 0.85],
    ],
    dtype=float,
)

# Snapshot policy utility reordered from Attack, Guard, Approach, Retreat, Probe.
TOY_UTILITY = np.array(
    [
        [0.15, 0.15, 0.15],  # Wait (the snapshot's epistemic Probe policy)
        [0.30, 0.10, 1.00],  # Approach
        [0.00, 0.60, -0.10],  # Retreat
        [1.20, 0.30, 0.50],  # Attack
        [0.20, 1.10, 0.30],  # Guard
    ],
    dtype=float,
)

TOY_CUE_LIKELIHOOD = np.array(
    [
        [0.45, 0.15, 0.20],
        [0.15, 0.50, 0.15],
        [0.20, 0.10, 0.50],
        [0.20, 0.25, 0.15],
    ],
    dtype=float,
)


def _normalize(values: np.ndarray, axis: int | None = None) -> np.ndarray:
    values = np.asarray(values, dtype=float)
    return values / np.clip(values.sum(axis=axis, keepdims=True), 1e-12, None)


def _logsumexp(values: np.ndarray, axis: int | None = None) -> np.ndarray:
    maximum = np.max(values, axis=axis, keepdims=True)
    result = maximum + np.log(np.exp(values - maximum).sum(axis=axis, keepdims=True))
    return np.squeeze(result, axis=axis) if axis is not None else result.squeeze()


def _entropy(probabilities: np.ndarray, axis: int = -1) -> np.ndarray:
    clipped = np.clip(probabilities, 1e-12, 1.0)
    return -(clipped * np.log(clipped)).sum(axis=axis)


@dataclass
class ActiveInferenceModel:
    observation_likelihood: np.ndarray
    transition: np.ndarray
    initial_belief: np.ndarray
    habit_prior: np.ndarray
    utility: np.ndarray
    cue_likelihood: np.ndarray
    fit_log_likelihood: list[float]

    def save(self, path: str | Path) -> None:
        destination = Path(path)
        destination.parent.mkdir(parents=True, exist_ok=True)
        np.savez(
            destination,
            states=np.asarray(LATENT_STATES),
            observations=np.asarray(ACTION_CLASSES),
            actions=np.asarray(ACTION_CLASSES),
            observation_likelihood=self.observation_likelihood,
            transition=self.transition,
            initial_belief=self.initial_belief,
            habit_prior=self.habit_prior,
            utility=self.utility,
            cue_likelihood=self.cue_likelihood,
            fit_log_likelihood=np.asarray(self.fit_log_likelihood),
        )


def load_active_inference_model(path: str | Path) -> ActiveInferenceModel:
    archive = np.load(Path(path), allow_pickle=False)
    return ActiveInferenceModel(
        observation_likelihood=archive["observation_likelihood"],
        transition=archive["transition"],
        initial_belief=archive["initial_belief"],
        habit_prior=archive["habit_prior"],
        utility=archive["utility"],
        cue_likelihood=archive["cue_likelihood"],
        fit_log_likelihood=archive["fit_log_likelihood"].astype(float).tolist(),
    )


def _forward_backward(
    observations: np.ndarray,
    observation_likelihood: np.ndarray,
    transition: np.ndarray,
    initial_belief: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, float]:
    """Return state responsibilities and next/current transition counts."""
    state_count = transition.shape[0]
    length = len(observations)
    log_a = np.log(np.clip(observation_likelihood, 1e-12, 1.0))
    log_b = np.log(np.clip(transition, 1e-12, 1.0))
    log_initial = np.log(np.clip(initial_belief, 1e-12, 1.0))
    emission = np.zeros((length, state_count), dtype=float)
    valid = observations >= 0
    emission[valid] = log_a[observations[valid]]

    alpha = np.empty((length, state_count), dtype=float)
    alpha[0] = log_initial + emission[0]
    for index in range(1, length):
        alpha[index] = emission[index] + _logsumexp(
            log_b + alpha[index - 1][None, :], axis=1
        )
    log_likelihood = float(_logsumexp(alpha[-1]))

    beta = np.zeros((length, state_count), dtype=float)
    for index in range(length - 2, -1, -1):
        beta[index] = _logsumexp(
            log_b + (emission[index + 1] + beta[index + 1])[:, None], axis=0
        )
    gamma = np.exp(alpha + beta - log_likelihood)
    gamma = _normalize(gamma, axis=1)

    transitions = np.zeros((state_count, state_count), dtype=float)
    for index in range(length - 1):
        log_xi = (
            alpha[index][None, :]
            + log_b
            + (emission[index + 1] + beta[index + 1])[:, None]
            - log_likelihood
        )
        transitions += np.exp(log_xi)
    return gamma, transitions, log_likelihood


def fit_active_inference_model(
    feature_frame: pd.DataFrame,
    split_assignment: dict[str, str],
    iterations: int = 30,
    parameter_prior_strength: float = 8.0,
) -> ActiveInferenceModel:
    """Fit the discrete generative model with Baum-Welch on training sessions."""
    train = feature_frame[
        feature_frame["session_id"].astype(str).map(split_assignment).eq("train")
    ]
    sequences = [
        group["self_action_id"].map(action_id_to_class).to_numpy(dtype=int)
        for _, group in train.sort_values(["match_id", "player_id", "frame"]).groupby(
            ["match_id", "player_id"], sort=False
        )
        if len(group) > 1
    ]
    if not sequences:
        raise ValueError("No training sequences are available for active-inference fitting")

    observation_likelihood = _normalize(TOY_OBSERVATION_LIKELIHOOD, axis=0)
    transition = _normalize(TOY_TRANSITION, axis=0)
    initial_belief = np.full(len(LATENT_STATES), 1.0 / len(LATENT_STATES))
    history: list[float] = []

    for _ in range(iterations):
        observation_counts = parameter_prior_strength * TOY_OBSERVATION_LIKELIHOOD
        transition_counts = parameter_prior_strength * TOY_TRANSITION
        initial_counts = parameter_prior_strength * np.full(len(LATENT_STATES), 1 / len(LATENT_STATES))
        total_log_likelihood = 0.0
        for observations in sequences:
            gamma, xi, log_likelihood = _forward_backward(
                observations, observation_likelihood, transition, initial_belief
            )
            initial_counts += gamma[0]
            transition_counts += xi
            for observation_index in range(len(ACTION_CLASSES)):
                mask = observations == observation_index
                if mask.any():
                    observation_counts[observation_index] += gamma[mask].sum(axis=0)
            total_log_likelihood += log_likelihood
        observation_likelihood = _normalize(observation_counts, axis=0)
        transition = _normalize(transition_counts, axis=0)
        initial_belief = _normalize(initial_counts)
        history.append(total_log_likelihood)
        if len(history) > 1 and abs(history[-1] - history[-2]) < 1e-4:
            break

    voluntary = train["self_action_id"].map(action_id_to_class)
    counts = np.bincount(voluntary[voluntary >= 0].astype(int), minlength=len(ACTION_CLASSES)) + 1.0
    habit_prior = _normalize(counts)
    return ActiveInferenceModel(
        observation_likelihood=observation_likelihood,
        transition=transition,
        initial_belief=initial_belief,
        habit_prior=habit_prior,
        utility=TOY_UTILITY.copy(),
        cue_likelihood=_normalize(TOY_CUE_LIKELIHOOD, axis=0),
        fit_log_likelihood=history,
    )


def infer_frame_beliefs(
    feature_frame: pd.DataFrame,
    model: ActiveInferenceModel,
) -> np.ndarray:
    """Perform causal filtering; no future frame contributes to a row's belief."""
    beliefs = np.zeros((len(feature_frame), len(LATENT_STATES)), dtype=float)
    for _, indices in feature_frame.groupby(["match_id", "player_id"], sort=False).groups.items():
        belief = model.initial_belief.copy()
        for index in indices:
            belief = model.transition @ belief
            observation = action_id_to_class(feature_frame.at[index, "self_action_id"])
            if observation >= 0:
                belief *= model.observation_likelihood[observation]
                belief = _normalize(belief)
            beliefs[index] = belief
    return beliefs


def active_inference_probabilities(
    model: ActiveInferenceModel,
    beliefs: np.ndarray,
    horizon_frames: int,
    utility_scale: float = 1.0,
    information_gain_weight: float = 1.0,
    habit_scale: float = 1.0,
) -> np.ndarray:
    """Project beliefs to t+h and score five policies with expected free energy."""
    projected = beliefs @ np.linalg.matrix_power(model.transition, horizon_frames).T
    projected = _normalize(projected, axis=1)
    expected_utility = projected @ model.utility.T
    prior_entropy = _entropy(projected)
    cue_distribution = projected @ model.cue_likelihood.T
    wait_information_gain = np.maximum(0.0, prior_entropy - 0.5 * _entropy(cue_distribution))
    information_gain = np.repeat((0.10 * prior_entropy)[:, None], len(ACTION_CLASSES), axis=1)
    information_gain[:, 0] = wait_information_gain

    # G = -expected utility - epistemic value; lower G is preferred.
    logits = (
        habit_scale * np.log(np.clip(model.habit_prior, 1e-12, 1.0))[None, :]
        + utility_scale * expected_utility
        + information_gain_weight * information_gain
    )
    logits -= logits.max(axis=1, keepdims=True)
    probabilities = np.exp(logits)
    return _normalize(probabilities, axis=1)


def tune_scoring_parameters(
    model: ActiveInferenceModel,
    beliefs: np.ndarray,
    targets: np.ndarray,
    horizon_frames: int,
) -> tuple[dict[str, float], float]:
    """Select a small EFE scoring grid by validation-set log loss."""
    best_parameters: dict[str, float] | None = None
    best_loss = float("inf")
    for utility_scale in (0.5, 1.0, 2.0, 4.0):
        for information_gain_weight in (0.5, 1.0, 2.0):
            for habit_scale in (0.5, 1.0):
                probabilities = active_inference_probabilities(
                    model,
                    beliefs,
                    horizon_frames,
                    utility_scale=utility_scale,
                    information_gain_weight=information_gain_weight,
                    habit_scale=habit_scale,
                )
                loss = float(-np.log(np.clip(probabilities[np.arange(len(targets)), targets], 1e-15, 1.0)).mean())
                if loss < best_loss:
                    best_loss = loss
                    best_parameters = {
                        "utility_scale": utility_scale,
                        "information_gain_weight": information_gain_weight,
                        "habit_scale": habit_scale,
                    }
    assert best_parameters is not None
    return best_parameters, best_loss
