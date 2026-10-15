import math

from fastapi.testclient import TestClient

from kairo_agents.main import app

DAYS = 200


def series(level: float, phase: float = 0.0) -> list[float]:
    return [
        round(
            level
            * (1 + 0.3 * math.sin(day * 2 * math.pi / 7))
            * (1 + 0.2 * math.sin(day / 9 + phase)),
            3,
        )
        for day in range(DAYS)
    ]


def region(key: str, in_area: bool = True, level: float = 50) -> dict:
    return {
        "id": key,
        "inArea": in_area,
        "orders": series(level),
        "revenue": series(level * 900),
        "searches": [1000, 1200, 900],
    }


def test_the_proof_plan_route_designs_a_test() -> None:
    client = TestClient(app)

    response = client.post(
        "/plan/proof-plan",
        json={"days": DAYS, "maxDays": 30, "regions": [region(f"c{index}") for index in range(6)]},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["kpi"] == "orders"
    assert body["method"] == "placebo-geo/1"
    assert len(body["holdoutIds"]) == 2
    assert len(body["testIds"]) == 4
    assert body["windowDays"] in (14, 21, 28)
    assert body["dataDays"] == DAYS
    assert [entry["days"] for entry in body["lifts"]] == [14, 21, 28]
    assert {"id", "score", "share", "inArea"} <= set(body["candidates"][0])


def test_the_route_keeps_chosen_holdouts() -> None:
    client = TestClient(app)

    response = client.post(
        "/plan/proof-plan",
        json={
            "days": DAYS,
            "holdoutIds": ["c1"],
            "regions": [region(f"c{index}") for index in range(4)],
        },
    )

    assert response.status_code == 200
    assert response.json()["holdoutIds"] == ["c1"]
    assert response.json()["testIds"] == ["c0", "c2", "c3"]


def test_series_must_cover_the_same_days() -> None:
    client = TestClient(app)
    short = region("c1")
    short["orders"] = short["orders"][:-1]

    response = client.post(
        "/plan/proof-plan", json={"days": DAYS, "regions": [region("c0"), short]}
    )

    assert response.status_code == 422


def test_searches_must_cover_the_same_months() -> None:
    client = TestClient(app)
    other = region("c1")
    other["searches"] = [1, 2]

    response = client.post(
        "/plan/proof-plan", json={"days": DAYS, "regions": [region("c0"), other]}
    )

    assert response.status_code == 422


def test_negative_values_are_refused() -> None:
    client = TestClient(app)
    bad = region("c1")
    bad["orders"][0] = -1

    response = client.post("/plan/proof-plan", json={"days": DAYS, "regions": [bad]})

    assert response.status_code == 422
