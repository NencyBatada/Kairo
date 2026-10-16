import math

import numpy as np
from fastapi.testclient import TestClient

from kairo_agents.main import app
from kairo_agents.measurement.lift import measure, t_quantile

HISTORY = 364
TEST_DAYS = 14
DAYS = HISTORY + TEST_DAYS


def world(rng: np.random.Generator, lift: float = 0.0) -> tuple[np.ndarray, np.ndarray]:
    days = np.arange(DAYS)
    season = (1 + 0.3 * np.sin(days * 2 * math.pi / 7)) * (1 + 0.2 * np.sin(days / 40))
    test = rng.poisson(60 * season).astype(float)
    holdout = rng.poisson(40 * season).astype(float)
    test[HISTORY:] = rng.poisson(60 * season[HISTORY:] * (1 + lift)).astype(float)
    return test, holdout


def test_a_planted_lift_is_found_inside_the_range() -> None:
    test, holdout = world(np.random.default_rng(7), lift=0.25)

    reading = measure(test, holdout, HISTORY, TEST_DAYS)

    figures = reading.figures
    assert figures is not None
    assert figures.conclusive
    assert figures.low <= 0.25 <= figures.high
    assert figures.low > 0
    assert figures.extra > 0
    assert figures.extra_low < figures.extra < figures.extra_high
    assert math.isclose(figures.actual - figures.expected, figures.extra, abs_tol=0.01)
    assert len(reading.daily) == TEST_DAYS
    assert math.isclose(sum(day[1] for day in reading.daily), figures.expected, rel_tol=1e-3)


def test_no_effect_rarely_looks_conclusive() -> None:
    rng = np.random.default_rng(11)
    runs = 300
    false = 0
    for _ in range(runs):
        test, holdout = world(rng)
        figures = measure(test, holdout, HISTORY, TEST_DAYS).figures
        assert figures is not None
        false += figures.conclusive

    assert false / runs <= 0.15


def test_short_history_is_insufficient() -> None:
    test, holdout = world(np.random.default_rng(3))

    reading = measure(test[HISTORY - 40 :], holdout[HISTORY - 40 :], 40, TEST_DAYS)

    assert reading.figures is None
    assert reading.reason == "short_history"


def test_too_few_placebo_windows_is_insufficient() -> None:
    test, holdout = world(np.random.default_rng(3))
    start = 56 + TEST_DAYS + 7

    reading = measure(test[HISTORY - start :], holdout[HISTORY - start :], start, TEST_DAYS)

    assert reading.figures is None
    assert reading.reason == "few_placebos"


def test_t_quantile_widens_small_samples() -> None:
    assert math.isclose(t_quantile(2), 2.92, abs_tol=0.05)
    assert math.isclose(t_quantile(10), 1.812, abs_tol=0.01)
    assert math.isclose(t_quantile(1000), 1.645, abs_tol=0.01)


def regions(test: np.ndarray, holdout: np.ndarray, revenue: bool = True) -> list[dict]:
    half = [test * 0.5, test * 0.5]
    rows = [
        {"id": f"t{index}", "test": True, "outcome": values.tolist()}
        for index, values in enumerate(half)
    ]
    rows.append({"id": "h0", "test": False, "outcome": holdout.tolist()})
    if revenue:
        for row in rows:
            row["revenue"] = [value * 900 for value in row["outcome"]]
    return rows


def post(body: dict):
    return TestClient(app).post("/prove/lift", json=body)


def test_the_lift_route_measures_the_outcome_revenue_and_each_region() -> None:
    test, holdout = world(np.random.default_rng(5), lift=0.3)

    response = post(
        {
            "kpi": "orders",
            "days": DAYS,
            "start": HISTORY,
            "testDays": TEST_DAYS,
            "regions": regions(test, holdout),
        }
    )

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["reason"] is None
    assert body["method"] == "placebo-did/1"
    assert body["outcome"]["conclusive"]
    assert body["outcome"]["low"] <= body["outcome"]["lift"] <= body["outcome"]["high"]
    assert math.isclose(body["revenue"]["lift"], body["outcome"]["lift"], abs_tol=1e-3)
    assert [region["id"] for region in body["regions"]] == ["t0", "t1"]
    assert all(region["status"] == "ok" for region in body["regions"])
    assert len(body["daily"]) == TEST_DAYS
    assert set(body["daily"][0]) == {"actual", "expected"}


def test_revenue_is_left_out_when_a_region_has_none() -> None:
    test, holdout = world(np.random.default_rng(5))
    rows = regions(test, holdout)
    del rows[-1]["revenue"]

    body = post(
        {"kpi": "orders", "days": DAYS, "start": HISTORY, "testDays": TEST_DAYS, "regions": rows}
    ).json()

    assert body["revenue"] is None
    assert body["outcome"] is not None


def test_regions_without_outcome_data_are_insufficient() -> None:
    body = post(
        {
            "kpi": "conversions",
            "days": DAYS,
            "start": HISTORY,
            "testDays": TEST_DAYS,
            "regions": [{"id": "t0", "test": True}, {"id": "h0", "test": False}],
        }
    ).json()

    assert body["status"] == "insufficient_data"
    assert body["reason"] == "no_outcome"
    assert body["outcome"] is None


def test_the_lift_route_rejects_bad_requests() -> None:
    test, holdout = world(np.random.default_rng(5))
    rows = regions(test, holdout, revenue=False)

    no_holdout = post(
        {
            "kpi": "orders",
            "days": DAYS,
            "start": HISTORY,
            "testDays": TEST_DAYS,
            "regions": rows[:2],
        }
    )
    past_the_end = post(
        {"kpi": "orders", "days": DAYS, "start": HISTORY, "testDays": 20, "regions": rows}
    )
    short = post(
        {
            "kpi": "orders",
            "days": DAYS,
            "start": HISTORY,
            "testDays": TEST_DAYS,
            "regions": [{**rows[0], "outcome": [1.0, 2.0]}, rows[2]],
        }
    )

    assert no_holdout.status_code == 422
    assert past_the_end.status_code == 422
    assert short.status_code == 422
