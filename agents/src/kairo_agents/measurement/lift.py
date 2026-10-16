import math
from dataclasses import dataclass

import numpy as np

from kairo_agents.measurement.holdout import MIN_PLACEBOS, PRE_DAYS, log_lift, placebo_estimates

METHOD = "placebo-did/1"
Z_RANGE = 1.644854


@dataclass(frozen=True)
class Figures:
    lift: float
    low: float
    high: float
    actual: float
    expected: float
    extra: float
    extra_low: float
    extra_high: float
    conclusive: bool
    placebos: int
    spread: float


@dataclass(frozen=True)
class Reading:
    figures: Figures | None
    reason: str | None
    daily: list[tuple[float, float]]


def t_quantile(df: int, z: float = Z_RANGE) -> float:
    v = float(df)
    return (
        z
        + (z**3 + z) / (4 * v)
        + (5 * z**5 + 16 * z**3 + 3 * z) / (96 * v**2)
        + (3 * z**7 + 19 * z**5 + 17 * z**3 - 15 * z) / (384 * v**3)
    )


def measure(test: np.ndarray, holdout: np.ndarray, start: int, days: int) -> Reading:
    if start < PRE_DAYS:
        return Reading(None, "short_history", [])
    estimate = log_lift(test, holdout, start, days)
    if estimate is None:
        return Reading(None, "no_outcome", [])
    placebos = placebo_estimates(test[:start], holdout[:start], days)
    if len(placebos) < MIN_PLACEBOS:
        return Reading(None, "few_placebos", [])

    count = len(placebos)
    spread = float(np.std(placebos, ddof=1))
    half = t_quantile(count - 1) * spread * math.sqrt(1 + 1 / count)
    low_log, high_log = estimate - half, estimate + half
    actual = float(test[start : start + days].sum())
    ratio = float(test[start - PRE_DAYS : start].sum() / holdout[start - PRE_DAYS : start].sum())
    daily = [
        (float(test[day]), round(ratio * float(holdout[day]), 4))
        for day in range(start, start + days)
    ]
    figures = Figures(
        lift=round(math.exp(estimate) - 1, 4),
        low=round(math.exp(low_log) - 1, 4),
        high=round(math.exp(high_log) - 1, 4),
        actual=round(actual, 4),
        expected=round(actual / math.exp(estimate), 4),
        extra=round(extra(actual, estimate), 4),
        extra_low=round(extra(actual, low_log), 4),
        extra_high=round(extra(actual, high_log), 4),
        conclusive=low_log > 0 or high_log < 0,
        placebos=count,
        spread=round(spread, 6),
    )
    return Reading(figures, None, daily)


def extra(actual: float, log_effect: float) -> float:
    return actual - actual / math.exp(log_effect)


def pooled(series: list[np.ndarray | None], length: int) -> np.ndarray | None:
    present = [values for values in series if values is not None]
    if not present:
        return None
    total = np.zeros(length)
    for values in present:
        total += values
    return total
