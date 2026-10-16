import math

import numpy as np

from kairo_agents.measurement.lab import chart, check, fit
from kairo_agents.measurement.lift import measure

HISTORY = 364
CHANGE = 28
DAYS = HISTORY + CHANGE


def season() -> np.ndarray:
    days = np.arange(DAYS)
    return (1 + 0.3 * np.sin(days * 2 * math.pi / 7)) * (1 + 0.2 * np.sin(days / 40))


def world(rng: np.random.Generator, lift: float = 0.0) -> dict[str, np.ndarray]:
    shape = season()
    running = rng.poisson(80 * shape).astype(float)
    running[HISTORY:] = rng.poisson(80 * shape[HISTORY:] * (1 + lift)).astype(float)
    return {
        "running": running,
        "walking": rng.poisson(50 * shape).astype(float),
        "sandals": rng.poisson(30 * shape).astype(float),
        "noise": rng.poisson(40, DAYS).astype(float),
        "empty": np.zeros(DAYS),
    }


def test_a_planted_effect_is_found_inside_the_range() -> None:
    series = world(np.random.default_rng(5), lift=0.3)
    test = series["running"]
    holdout = series["walking"] + series["sandals"]

    figures = measure(test, holdout, HISTORY, CHANGE).figures

    assert figures is not None
    assert figures.conclusive
    assert figures.low <= 0.3 <= figures.high


def test_no_effect_rarely_looks_conclusive() -> None:
    rng = np.random.default_rng(13)
    runs = 200
    false = 0
    for _ in range(runs):
        series = world(rng)
        figures = measure(series["running"], series["walking"], HISTORY, CHANGE).figures
        assert figures is not None
        false += figures.conclusive

    assert false / runs <= 0.12


def test_suggestions_are_the_closest_untouched_series() -> None:
    series = world(np.random.default_rng(3))

    result = check(series, ["running"], None, HISTORY, CHANGE, CHANGE)

    keys = [item.key for item in result.candidates]
    assert "running" not in keys
    assert keys[:2] == ["walking", "sandals"] or keys[:2] == ["sandals", "walking"]
    assert result.comparison == [item.key for item in result.candidates if item.suggested]
    assert set(result.comparison) == {"walking", "sandals"}
    assert next(item for item in result.candidates if item.key == "empty").fit is None
    assert result.fit is not None and result.fit >= 0.7
    assert result.warnings == []
    assert result.placebos >= 3
    assert result.detectable is not None


def test_a_chosen_comparison_replaces_the_suggestion() -> None:
    series = world(np.random.default_rng(3))

    result = check(series, ["running"], ["noise"], HISTORY, CHANGE, CHANGE)

    assert result.comparison == ["noise"]
    assert result.warnings[0].code == "weak_comparison"
    assert result.warnings[0].value == result.fit


def test_each_weakness_is_warned_about() -> None:
    series = world(np.random.default_rng(3))
    short = {key: values[HISTORY - 30 :] for key, values in series.items()}

    result = check(short, ["running"], ["walking"], 30, CHANGE - 7, CHANGE)

    codes = {warning.code: warning.value for warning in result.warnings}
    assert codes["short_history"] == 30
    assert codes["not_covered"] == CHANGE - 7

    nothing = check(series, ["running"], [], HISTORY, CHANGE, CHANGE)
    assert [warning.code for warning in nothing.warnings] == ["no_comparison"]
    assert nothing.fit is None


def test_noisy_data_is_warned_about() -> None:
    rng = np.random.default_rng(9)
    series = {
        "affected": rng.poisson(3, DAYS).astype(float) + 1,
        "other": rng.poisson(3, DAYS).astype(float) + 1,
    }

    result = check(series, ["affected"], ["other"], HISTORY, 7, 7)

    noisy = [warning for warning in result.warnings if warning.code == "noisy"]
    assert noisy and noisy[0].value is not None and noisy[0].value > 0.2


def test_too_few_placebo_windows_are_warned_about() -> None:
    series = world(np.random.default_rng(3))
    trimmed = {key: values[HISTORY - 70 :] for key, values in series.items()}

    result = check(trimmed, ["running"], ["walking"], 70, CHANGE, CHANGE)

    assert result.detectable is None
    assert "few_placebos" in [warning.code for warning in result.warnings]


def test_sparse_series_are_compared_by_week() -> None:
    days = np.arange(140)
    affected = np.where(days % 7 == 0, 10 + (days // 7) % 3, 0).astype(float)
    other = np.where(days % 7 == 3, 20 + 2 * ((days // 7) % 3), 0).astype(float)

    assert fit(affected, other, 140) is not None
    assert fit(affected, other, 140) > 0.9


def test_the_chart_covers_the_pre_period_and_the_change() -> None:
    series = world(np.random.default_rng(3))

    days = chart(series["running"], series["walking"], HISTORY, CHANGE)

    assert len(days) == 56 + CHANGE
    assert days[0].offset == -56
    assert days[-1].offset == CHANGE - 1
    pre = days[:56]
    assert math.isclose(
        sum(day.actual for day in pre), sum(day.expected for day in pre), rel_tol=1e-3
    )
    assert chart(np.zeros(DAYS), series["walking"], HISTORY, CHANGE) == []
