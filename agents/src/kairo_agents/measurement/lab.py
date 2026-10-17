import math
from dataclasses import dataclass

import numpy as np

from kairo_agents.measurement.holdout import (
    MIN_PLACEBOS,
    PRE_DAYS,
    Z_POWER,
    correlation,
    placebo_estimates,
    weekly,
)
from kairo_agents.measurement.lift import pooled

SUGGEST_COUNT = 5
SUGGEST_FIT = 0.5
WEAK_FIT = 0.7
NOISY_EFFECT = 0.20
SPARSE_SHARE = 0.5


@dataclass(frozen=True)
class Candidate:
    key: str
    fit: float | None
    suggested: bool


@dataclass(frozen=True)
class Warning:
    code: str
    value: float | None


@dataclass(frozen=True)
class Check:
    candidates: list[Candidate]
    comparison: list[str]
    fit: float | None
    detectable: float | None
    placebos: int
    warnings: list[Warning]


@dataclass(frozen=True)
class Day:
    offset: int
    actual: float
    expected: float


def fit(affected: np.ndarray, other: np.ndarray, start: int) -> float | None:
    left = affected[max(0, start - PRE_DAYS) : start]
    right = other[max(0, start - PRE_DAYS) : start]
    if sparse(left) or sparse(right):
        left, right = weekly(left), weekly(right)
    value = correlation(left, right)
    return None if value is None or math.isnan(value) else round(value, 4)


def sparse(values: np.ndarray) -> bool:
    return values.size > 0 and float(np.mean(values == 0)) >= SPARSE_SHARE


def candidates(
    series: dict[str, np.ndarray], affected: np.ndarray, excluded: set[str], start: int
) -> list[Candidate]:
    scored = [
        (key, fit(affected, values, start)) for key, values in series.items() if key not in excluded
    ]
    scored.sort(key=lambda item: (item[1] is None, -(item[1] or 0.0), item[0]))
    top = [key for key, value in scored if value is not None and value >= SUGGEST_FIT]
    top = top[:SUGGEST_COUNT]
    return [Candidate(key, value, key in top) for key, value in scored]


def detectable(
    test: np.ndarray, holdout: np.ndarray, start: int, days: int
) -> tuple[float | None, int]:
    estimates = placebo_estimates(test[:start], holdout[:start], days)
    if len(estimates) < MIN_PLACEBOS:
        return None, len(estimates)
    spread = float(np.std(estimates, ddof=1))
    return round(math.exp(Z_POWER * spread) - 1, 4), len(estimates)


def check(
    series: dict[str, np.ndarray],
    affected: list[str],
    comparison: list[str] | None,
    start: int,
    days: int,
    planned_days: int,
) -> Check:
    length = next(iter(series.values())).size
    test = pooled([series[key] for key in affected], length)
    if test is None:
        raise ValueError("Pick at least one affected series.")
    ranked = candidates(series, test, set(affected), start)
    chosen = (
        comparison if comparison is not None else [item.key for item in ranked if item.suggested]
    )
    holdout = pooled([series[key] for key in chosen], length)
    pooled_fit = fit(test, holdout, start) if holdout is not None else None
    effect, placebos = detectable(test, holdout, start, days) if holdout is not None else (None, 0)

    warnings: list[Warning] = []
    if start < PRE_DAYS:
        warnings.append(Warning("short_history", float(start)))
    if not chosen:
        warnings.append(Warning("no_comparison", None))
    elif pooled_fit is None or pooled_fit < WEAK_FIT:
        warnings.append(Warning("weak_comparison", pooled_fit))
    if chosen and start >= PRE_DAYS and placebos < MIN_PLACEBOS:
        warnings.append(Warning("few_placebos", float(placebos)))
    if effect is not None and effect > NOISY_EFFECT:
        warnings.append(Warning("noisy", effect))
    if days < planned_days:
        warnings.append(Warning("not_covered", float(days)))
    return Check(ranked, chosen, pooled_fit, effect, placebos, warnings)


def chart(test: np.ndarray, holdout: np.ndarray, start: int, days: int) -> list[Day]:
    first = max(0, start - PRE_DAYS)
    pre_test = float(test[first:start].sum())
    pre_holdout = float(holdout[first:start].sum())
    if pre_test <= 0 or pre_holdout <= 0:
        return []
    ratio = pre_test / pre_holdout
    return [
        Day(day - start, float(test[day]), round(ratio * float(holdout[day]), 4))
        for day in range(first, start + days)
    ]
