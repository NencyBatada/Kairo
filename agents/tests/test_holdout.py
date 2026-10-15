import math

import numpy as np
import pytest

from kairo_agents.measurement.holdout import (
    MAX_SHARE,
    PRE_DAYS,
    Z_POWER,
    RegionData,
    design,
    placebo_lift,
)

DAYS = 364


def common(rng: np.random.Generator, days: int = DAYS) -> np.ndarray:
    week = 1 + 0.3 * np.sin(np.arange(days) * 2 * np.pi / 7)
    season = 1 + 0.4 * np.sin(np.arange(days) * 2 * np.pi / 120)
    return week * season


def city(
    rng: np.random.Generator,
    key: str,
    level: float,
    shape: np.ndarray,
    in_area: bool = True,
    noise: float = 0.05,
    revenue: bool = True,
) -> RegionData:
    orders = level * shape * np.exp(rng.normal(0, noise, shape.size))
    return RegionData(
        id=key,
        in_area=in_area,
        orders=orders,
        conversions=orders * 0.5,
        revenue=orders * 1000 if revenue else None,
    )


def test_regions_that_move_with_the_area_score_higher_and_are_held_out() -> None:
    rng = np.random.default_rng(1)
    shape = common(rng)
    odd = 1 + 0.6 * np.sin(np.arange(DAYS) * 2 * np.pi / 45 + 1.3)
    regions = [city(rng, f"c{index}", 40, shape) for index in range(8)]
    regions.append(city(rng, "odd", 40, odd))

    plan = design(regions)

    scores = {candidate.id: candidate.score for candidate in plan.candidates}
    assert scores["odd"] < min(score for key, score in scores.items() if key != "odd")
    assert "odd" not in plan.holdout_ids
    assert plan.inside_area
    assert plan.kpi == "orders"
    assert set(plan.test_ids) | set(plan.holdout_ids) == {region.id for region in regions}
    assert not set(plan.test_ids) & set(plan.holdout_ids)


def test_default_holdouts_hold_a_small_share_of_the_area() -> None:
    rng = np.random.default_rng(2)
    shape = common(rng)
    regions = [city(rng, f"c{index}", 30, shape) for index in range(10)]

    plan = design(regions)

    assert len(plan.holdout_ids) == 2
    assert 0.15 <= plan.holdout_share <= MAX_SHARE


def test_two_holdouts_are_kept_even_when_regions_are_large() -> None:
    rng = np.random.default_rng(3)
    shape = common(rng)
    regions = [city(rng, f"c{index}", 30, shape) for index in range(3)]

    plan = design(regions)

    assert len(plan.holdout_ids) == 2
    assert len(plan.test_ids) == 1


def test_smallest_detectable_lift_matches_the_noise_in_the_data() -> None:
    rng = np.random.default_rng(4)
    noise = 0.2
    test = 1000 * np.exp(rng.normal(0, noise, 2000))
    holdout = 1000 * np.exp(rng.normal(0, noise, 2000))

    measured = placebo_lift(test, holdout, 14)

    per_day = math.exp(noise**2) - 1
    spread = math.sqrt(2 * per_day * (1 / 14 + 1 / PRE_DAYS))
    expected = math.exp(Z_POWER * spread) - 1
    assert measured.lift == pytest.approx(expected, rel=0.25)
    assert measured.placebos > 200


def test_busy_regions_reach_the_target_in_two_weeks() -> None:
    rng = np.random.default_rng(5)
    shape = common(rng)
    regions = [city(rng, f"c{index}", 400, shape, noise=0.03) for index in range(10)]

    plan = design(regions)

    assert plan.window_days == 14
    assert plan.lift <= 0.10
    assert plan.placebos >= 20
    assert plan.warnings == []
    assert plan.holdout_cost == pytest.approx(plan.holdout_daily_revenue * 14 * plan.lift, rel=1e-6)


def test_quiet_regions_get_the_longest_window_and_a_warning() -> None:
    rng = np.random.default_rng(6)
    shape = common(rng)
    regions = [
        RegionData(id=f"c{index}", in_area=True, orders=rng.poisson(2 * shape).astype(float))
        for index in range(5)
    ]

    plan = design(regions)

    lifts = {entry.days: entry.lift for entry in plan.lifts}
    assert lifts[28] < lifts[14]
    assert plan.window_days == 28
    assert "lift_too_high" in [warning.code for warning in plan.warnings]
    assert plan.holdout_cost is None


def test_thin_history_warns_and_has_no_lift() -> None:
    rng = np.random.default_rng(7)
    shape = common(rng, 40)
    regions = [RegionData(id=f"c{index}", in_area=True, orders=40 * shape) for index in range(5)]

    plan = design(regions)

    codes = [warning.code for warning in plan.warnings]
    assert "short_history" in codes
    assert "no_lift_estimate" in codes
    assert plan.lift is None
    assert plan.data_days == 40


def test_history_before_the_first_sale_is_ignored() -> None:
    rng = np.random.default_rng(8)
    shape = common(rng)
    regions = []
    for index in range(5):
        orders = 50 * shape
        orders[:320] = 0
        regions.append(RegionData(id=f"c{index}", in_area=True, orders=orders))

    plan = design(regions)

    assert plan.data_days == DAYS - 320
    assert "short_history" in [warning.code for warning in plan.warnings]


def test_a_single_region_campaign_uses_controls_outside_it() -> None:
    rng = np.random.default_rng(9)
    shape = common(rng)
    regions = [city(rng, "mumbai", 100, shape)]
    regions += [city(rng, f"o{index}", 60, shape, in_area=False) for index in range(5)]

    plan = design(regions)

    assert not plan.inside_area
    assert plan.test_ids == ["mumbai"]
    assert len(plan.holdout_ids) == 3
    assert all(key.startswith("o") for key in plan.holdout_ids)
    assert plan.holdout_cost == 0
    assert "outside_controls" in [warning.code for warning in plan.warnings]


def test_chosen_holdouts_are_kept() -> None:
    rng = np.random.default_rng(10)
    shape = common(rng)
    regions = [city(rng, f"c{index}", 40, shape) for index in range(6)]

    plan = design(regions, fixed_holdouts=["c5", "c4", "c5", "missing"])

    assert plan.holdout_ids == ["c5", "c4"]
    assert plan.test_ids == ["c0", "c1", "c2", "c3"]


def test_without_sales_the_test_measures_conversions() -> None:
    rng = np.random.default_rng(11)
    shape = common(rng)
    regions = [
        RegionData(
            id=f"c{index}", in_area=True, conversions=30 * shape * np.exp(rng.normal(0, 0.05, DAYS))
        )
        for index in range(5)
    ]

    plan = design(regions)

    assert plan.kpi == "conversions"
    assert "conversions_kpi" in [warning.code for warning in plan.warnings]
    assert plan.lift is not None


def test_a_short_campaign_caps_the_window() -> None:
    rng = np.random.default_rng(12)
    shape = common(rng)
    regions = [city(rng, f"c{index}", 40, shape) for index in range(6)]

    plan = design(regions, max_days=10)

    assert plan.window_days == 10
    assert "short_campaign" in [warning.code for warning in plan.warnings]


def test_a_campaign_shorter_than_three_weeks_only_tries_two_weeks() -> None:
    rng = np.random.default_rng(13)
    shape = common(rng)
    regions = [city(rng, f"c{index}", 40, shape) for index in range(6)]

    plan = design(regions, max_days=20)

    assert [entry.days for entry in plan.lifts] == [14]


def test_no_history_in_the_area_designs_nothing() -> None:
    rng = np.random.default_rng(14)
    shape = common(rng)
    regions = [city(rng, f"o{index}", 40, shape, in_area=False) for index in range(4)]
    regions.append(RegionData(id="new", in_area=True, orders=np.zeros(DAYS)))

    plan = design(regions)

    assert plan.holdout_ids == []
    assert "no_area_history" in [warning.code for warning in plan.warnings]


def test_search_demand_counts_towards_the_match() -> None:
    rng = np.random.default_rng(15)
    shape = common(rng)
    months = 1 + 0.5 * np.sin(np.arange(12) * 2 * np.pi / 12)
    regions = []
    for index in range(5):
        base = city(rng, f"c{index}", 40, shape)
        regions.append(
            RegionData(
                id=base.id,
                in_area=True,
                orders=base.orders,
                searches=1000 * (months if index < 4 else months[::-1]),
            )
        )

    plan = design(regions)

    scores = {candidate.id: candidate.score for candidate in plan.candidates}
    assert scores["c4"] < min(scores[f"c{index}"] for index in range(4))
