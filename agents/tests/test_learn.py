import json
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

from kairo_agents.agents.learn.api import client_source
from kairo_agents.agents.learn.lessons import clean_lessons
from kairo_agents.agents.learn.models import DraftLesson, DraftLessons, LearnRequest
from kairo_agents.agents.learn.prompt import LESSONS_PROMPT_VERSION, lessons_instructions
from kairo_agents.agents.sense.prompt import data_block
from kairo_agents.main import app

REQUEST = {
    "brand": "Acme Shoes",
    "currency": "INR",
    "campaign": {
        "title": "Monsoon runners",
        "kpi": "orders",
        "objective": "Sell waterproof shoes while monsoon searches peak.",
        "audiences": ["Commuters caught in the rain"],
        "keyMessages": ["Dries in minutes."],
        "languages": ["en", "hi"],
        "products": [{"id": "p-1", "title": "Monsoon Runner", "category": "Shoes > Rain"}],
    },
    "result": {
        "outcome": "worked",
        "lift": 0.12,
        "low": 0.06,
        "high": 0.18,
        "extra": 420,
        "extraLow": 210,
        "extraHigh": 630,
        "costPerExtra": 310,
        "spend": 130200,
        "results": 3900,
    },
    "regions": [
        {
            "name": "Mumbai",
            "test": True,
            "conclusive": True,
            "lift": 0.14,
            "low": 0.05,
            "high": 0.23,
            "extra": 300,
        },
        {
            "name": "Pune",
            "test": True,
            "conclusive": False,
            "lift": 0.08,
            "low": -0.04,
            "high": 0.2,
        },
        {"name": "Nagpur", "test": False},
    ],
    "ads": [
        {
            "id": "ad:search:1",
            "campaignType": "search",
            "language": "en",
            "headlines": ["Dries in minutes"],
            "impressions": 20000,
            "clicks": 1000,
            "conversions": 60,
        },
        {
            "id": "ad:search:2",
            "campaignType": "search",
            "language": "en",
            "headlines": ["Lowest prices"],
            "impressions": 20000,
            "clicks": 1000,
            "conversions": 20,
        },
        {
            "id": "ad:dg:1",
            "campaignType": "demand_gen",
            "language": "hi",
            "headlines": ["Barish mein bhi sukha"],
            "visual": "A runner on a rainy Mumbai street",
            "impressions": 50000,
            "clicks": 900,
            "conversions": 30,
        },
        {
            "id": "ad:dg:2",
            "campaignType": "demand_gen",
            "language": "en",
            "headlines": ["Studio shot"],
            "visual": "The shoe on a white background",
            "impressions": 50000,
            "clicks": 300,
            "conversions": 1,
        },
    ],
}

LESSONS = {
    "lessons": [
        {
            "kind": "message",
            "outcome": "worked",
            "title": "Quick-dry beats price",
            "lesson": "Quick-dry claims converted three times as well as price claims.",
            "adIds": ["ad:search:1"],
            "evidence": "6% against 2% of clicks became orders.",
        },
        {
            "kind": "message",
            "outcome": "didnt_work",
            "title": "Price claims fell flat",
            "lesson": "Leading with price converted poorly.",
            "adIds": ["ad:search:2"],
        },
        {
            "kind": "visual",
            "outcome": "worked",
            "title": "Rainy streets",
            "lesson": "Rainy street scenes worked for Demand Gen.",
            "adIds": ["ad:dg:1"],
        },
        {
            "kind": "region",
            "outcome": "worked",
            "title": "Mumbai responds",
            "lesson": "Mumbai beat its holdout.",
            "regions": ["mumbai", "Goa"],
        },
        {
            "kind": "audience",
            "outcome": "worked",
            "title": "Commuters",
            "lesson": "Commuters caught in the rain bought.",
        },
    ]
}


class FakeModels:
    def __init__(self) -> None:
        self.text = json.dumps(LESSONS)
        self.error: Exception | None = None
        self.calls: list[dict] = []

    async def generate_content(self, **kwargs):
        self.calls.append(kwargs)
        if self.error is not None:
            raise self.error
        return SimpleNamespace(text=self.text, candidates=[], usage_metadata=None)


@pytest.fixture
def models():
    fake = FakeModels()
    client = SimpleNamespace(aio=SimpleNamespace(models=fake))
    app.dependency_overrides[client_source] = lambda: lambda: client
    yield fake
    app.dependency_overrides.clear()


def request(**changes) -> LearnRequest:
    return LearnRequest.model_validate({**REQUEST, **changes})


def draft(**fields) -> DraftLesson:
    base = {"kind": "message", "outcome": "worked", "title": "A lesson", "lesson": "It worked."}
    return DraftLesson.model_validate({**base, **fields})


def test_lessons_make_one_structured_call_and_keep_the_supported_lessons(
    models, monkeypatch
) -> None:
    monkeypatch.setenv("KAIRO_LEARN_MODEL", "gemini-flash-test")

    response = TestClient(app).post("/learn/lessons", json=REQUEST)

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "gemini-flash-test"
    assert body["promptVersion"] == LESSONS_PROMPT_VERSION
    titles = [lesson["title"] for lesson in body["lessons"]]
    assert titles == [
        "Quick-dry beats price",
        "Price claims fell flat",
        "Rainy streets",
        "Mumbai responds",
        "Commuters",
    ]
    first = body["lessons"][0]
    assert first["adIds"] == ["ad:search:1"]
    assert first["languages"] == ["en"]
    assert first["evidence"]["lift"] == 0.12
    assert first["evidence"]["ads"][0]["rate"] == 0.06
    assert first["evidence"]["note"] == "6% against 2% of clicks became orders."
    assert body["lessons"][2]["languages"] == ["hi"]
    assert body["lessons"][3]["regions"] == ["Mumbai"]
    assert body["lessons"][3]["evidence"]["regions"] == [
        {
            "name": "Mumbai",
            "conclusive": True,
            "lift": 0.14,
            "low": 0.05,
            "high": 0.23,
            "extra": 300.0,
        }
    ]
    assert body["lessons"][4]["languages"] == ["en", "hi"]
    [call] = models.calls
    assert call["config"].response_schema is DraftLessons
    assert call["config"].system_instruction == lessons_instructions()
    assert call["contents"] == data_block(LearnRequest.model_validate(REQUEST))


def test_an_inconclusive_result_saves_no_lessons_and_skips_the_model(models) -> None:
    result = {**REQUEST["result"], "outcome": "inconclusive"}

    response = TestClient(app).post("/learn/lessons", json={**REQUEST, "result": result})

    assert response.status_code == 200
    assert response.json()["lessons"] == []
    assert response.json()["model"] is None
    assert models.calls == []


def test_a_model_error_is_reported(models) -> None:
    models.error = RuntimeError("quota")

    response = TestClient(app).post("/learn/lessons", json=REQUEST)

    assert response.status_code == 502
    assert response.json() == {"error": "model_failed"}


def test_ads_without_enough_data_cannot_back_a_lesson() -> None:
    thin = [{**ad, "clicks": 50, "conversions": 2} for ad in REQUEST["ads"]]

    lessons = clean_lessons([draft(adIds=["ad:search:1"])], request(ads=thin))

    assert lessons == []


def test_ads_without_a_clear_gap_cannot_back_a_lesson() -> None:
    close = [
        {**REQUEST["ads"][0], "conversions": 60},
        {**REQUEST["ads"][1], "conversions": 55},
    ]

    lessons = clean_lessons([draft(adIds=["ad:search:1"])], request(ads=close))

    assert lessons == []


def test_a_lesson_must_match_which_side_of_the_gap_its_ads_are_on() -> None:
    lessons = clean_lessons(
        [
            draft(title="Wrong winner", adIds=["ad:search:2"]),
            draft(title="Wrong loser", outcome="didnt_work", adIds=["ad:search:1"]),
            draft(title="Unknown ad", adIds=["ad:search:9"]),
            draft(title="Mixed ads", adIds=["ad:search:1", "ad:search:9"]),
            draft(title="Visual without one", kind="visual", adIds=["ad:search:1"]),
        ],
        request(),
    )

    assert lessons == []


def test_no_effect_turns_audience_and_region_lessons_into_what_did_not_work() -> None:
    result = {**REQUEST["result"], "outcome": "no_effect"}
    lessons = clean_lessons(
        [
            draft(title="Claimed win", kind="audience", outcome="worked"),
            draft(title="Commuters ignored it", kind="audience", outcome="didnt_work"),
            draft(title="No region", kind="region", outcome="didnt_work", regions=["Goa"]),
        ],
        request(result=result),
    )

    assert [lesson.title for lesson in lessons] == ["Commuters ignored it"]


def test_lessons_are_cleaned_capped_and_unique() -> None:
    drafts = [draft(kind="audience", title=f"Lesson {index}") for index in range(12)]
    drafts.insert(1, draft(kind="audience", title=" lesson 0 "))
    drafts.insert(2, draft(kind="Opinion", title="Unknown kind"))
    drafts.insert(3, draft(kind="audience", title="  ", lesson="No title"))

    lessons = clean_lessons(drafts, request())

    assert [lesson.title for lesson in lessons] == [f"Lesson {index}" for index in range(8)]


def test_requests_refuse_unknown_outcomes(models) -> None:
    result = {**REQUEST["result"], "outcome": "maybe"}

    response = TestClient(app).post("/learn/lessons", json={**REQUEST, "result": result})

    assert response.status_code == 422
    assert models.calls == []


def search_ads(*conversions: int, clicks: int = 1000) -> list[dict]:
    return [
        {
            "id": f"ad:search:{index}",
            "campaignType": "search",
            "impressions": clicks * 20,
            "clicks": clicks,
            "conversions": converted,
        }
        for index, converted in enumerate(conversions, start=1)
    ]


def test_each_ad_is_compared_with_the_rest_of_its_group_so_one_ad_cannot_win_and_lose() -> None:
    ads = search_ads(10, 20, 40)
    drafts = [
        draft(title="Best", adIds=["ad:search:3"]),
        draft(title="Worst", outcome="didnt_work", adIds=["ad:search:1"]),
        draft(title="Middle won", adIds=["ad:search:2"]),
        draft(title="Middle lost", outcome="didnt_work", adIds=["ad:search:2"]),
    ]

    lessons = clean_lessons(drafts, request(ads=ads))

    assert [lesson.title for lesson in lessons] == ["Best", "Worst"]


def test_a_gap_that_could_be_chance_is_not_a_lesson() -> None:
    ads = search_ads(10, 7, clicks=100)
    drafts = [
        draft(title="Winner", adIds=["ad:search:1"]),
        draft(title="Loser", outcome="didnt_work", adIds=["ad:search:2"]),
    ]

    assert clean_lessons(drafts, request(ads=ads)) == []


def test_region_lessons_must_match_that_regions_own_lift() -> None:
    drafts = [
        draft(title="Pune worked", kind="region", regions=["Pune"]),
        draft(title="Mumbai worked", kind="region", regions=["Mumbai"]),
        draft(title="Nagpur worked", kind="region", regions=["Nagpur"]),
    ]

    lessons = clean_lessons(drafts, request())

    assert [lesson.title for lesson in lessons] == ["Mumbai worked"]
    no_effect = {**REQUEST["result"], "outcome": "no_effect"}
    failed = clean_lessons(
        [
            draft(title="Mumbai failed", kind="region", outcome="didnt_work", regions=["Mumbai"]),
            draft(title="Pune failed", kind="region", outcome="didnt_work", regions=["Pune"]),
        ],
        request(result=no_effect),
    )
    assert [lesson.title for lesson in failed] == ["Pune failed"]
