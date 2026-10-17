import json
import math
from types import SimpleNamespace

import numpy as np
import pytest
from fastapi.testclient import TestClient

from kairo_agents.agents.prove.lab import allowed_numbers, clean_lab
from kairo_agents.agents.prove.lab_models import DraftedLab, LabFacts
from kairo_agents.agents.prove.prompt import LAB_PROMPT_VERSION, lab_instructions
from kairo_agents.agents.prove.report_api import client_source
from kairo_agents.agents.sense.prompt import DATA_END, DATA_START
from kairo_agents.main import app

HISTORY = 364
CHANGE = 28
DAYS = HISTORY + CHANGE


def series(lift: float = 0.3) -> list[dict]:
    rng = np.random.default_rng(5)
    days = np.arange(DAYS)
    shape = (1 + 0.3 * np.sin(days * 2 * math.pi / 7)) * (1 + 0.2 * np.sin(days / 40))
    running = rng.poisson(80 * shape).astype(float)
    running[HISTORY:] = rng.poisson(80 * shape[HISTORY:] * (1 + lift)).astype(float)
    walking = rng.poisson(50 * shape).astype(float)
    sandals = rng.poisson(30 * shape).astype(float)
    return [
        {"key": "running", "name": "Running shoes", "values": running.tolist()},
        {"key": "walking", "name": "Walking shoes", "values": walking.tolist()},
        {"key": "sandals", "name": "Sandals", "values": sandals.tolist()},
    ]


def data(**change) -> dict:
    return {
        "days": DAYS,
        "start": HISTORY,
        "changeDays": CHANGE,
        "plannedDays": CHANGE,
        "series": series(),
        "affected": ["running"],
        **change,
    }


def run(**change) -> dict:
    return {
        **data(comparison=["walking", "sandals"]),
        "name": "Running shoe price cut",
        "whatChanged": "A 10% price cut on running shoes",
        "metric": "items purchased",
        "source": "ga4",
        "currency": "INR",
        **change,
    }


GOOD = {
    "summary": "Running shoe sales rose against walking shoes and sandals.",
    "explanation": "The comparison tracked running shoes closely before the change.",
    "inconclusiveReason": "Ignored.",
    "nextAdvice": "Ignored too.",
}


class FakeModels:
    def __init__(self) -> None:
        self.text: str = json.dumps(GOOD)
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


def facts(**change) -> LabFacts:
    return LabFacts.model_validate(
        {
            "name": "Price cut",
            "whatChanged": "A 10% price cut",
            "metric": "items purchased",
            "source": "ga4",
            "currency": "INR",
            "affected": ["Running shoes"],
            "comparison": ["Walking shoes"],
            "changeDays": 28,
            "plannedDays": 28,
            "historyDays": 364,
            "minHistoryDays": 56,
            "outcome": "rose",
            "reason": None,
            "lift": 0.31,
            "low": 0.22,
            "high": 0.4,
            "actual": 2900,
            "expected": 2214,
            "extra": 686,
            "extraLow": 523,
            "extraHigh": 829,
            "fit": 0.93,
            "detectable": 0.12,
            "placebos": 40,
            "warnings": [],
            **change,
        }
    )


def test_check_suggests_a_comparison_and_reports_its_fit() -> None:
    response = TestClient(app).post("/prove/lab/check", json=data())

    assert response.status_code == 200
    body = response.json()
    assert set(body["comparison"]) == {"walking", "sandals"}
    assert {item["key"] for item in body["candidates"]} == {"walking", "sandals"}
    assert all(item["suggested"] for item in body["candidates"])
    assert body["fit"] > 0.7
    assert body["warnings"] == []
    assert body["placebos"] >= 3


def test_check_warns_about_a_short_history() -> None:
    trimmed = [{**item, "values": item["values"][HISTORY - 40 :]} for item in series()]

    response = TestClient(app).post(
        "/prove/lab/check", json=data(days=40 + CHANGE, start=40, series=trimmed)
    )

    assert {"code": "short_history", "value": 40.0} in response.json()["warnings"]


def test_run_measures_the_change_and_explains_it(models, monkeypatch) -> None:
    monkeypatch.setenv("KAIRO_PROVE_MODEL", "gemini-pro-test")

    response = TestClient(app).post("/prove/lab/run", json=run())

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["outcome"] == "rose"
    assert body["figures"]["conclusive"]
    assert body["figures"]["low"] <= 0.3 <= body["figures"]["high"]
    assert len(body["daily"]) == 56 + CHANGE
    assert body["daily"][0]["offset"] == -56
    assert body["inconclusiveReason"] is None
    assert body["method"] == "placebo-did/1"
    assert (body["model"], body["promptVersion"]) == ("gemini-pro-test", LAB_PROMPT_VERSION)
    [call] = models.calls
    assert call["config"].system_instruction == lab_instructions()
    assert call["config"].response_schema is DraftedLab
    assert call["contents"].startswith(DATA_START)
    assert call["contents"].endswith(DATA_END)
    sent = json.loads(call["contents"][len(DATA_START) : -len(DATA_END)])
    assert sent["affected"] == ["Running shoes"]
    assert sent["comparison"] == ["Walking shoes", "Sandals"]
    assert sent["outcome"] == "rose"


def test_thin_data_is_inconclusive_and_must_be_explained(models) -> None:
    trimmed = [{**item, "values": item["values"][HISTORY - 40 :]} for item in series()]
    request = run(days=40 + CHANGE, start=40, series=trimmed)
    models.text = json.dumps(GOOD | {"inconclusiveReason": " ", "nextAdvice": " "})

    assert TestClient(app).post("/prove/lab/run", json=request).status_code == 502

    models.text = json.dumps(
        {
            **GOOD,
            "inconclusiveReason": "There were only 40 days of history before the change.",
            "nextAdvice": "Wait until there are at least 8 weeks of history.",
        }
    )
    response = TestClient(app).post("/prove/lab/run", json=request)

    assert response.status_code == 200
    body = response.json()
    assert (body["status"], body["reason"], body["outcome"]) == (
        "insufficient_data",
        "short_history",
        "inconclusive",
    )
    assert body["figures"] is None
    assert body["nextAdvice"].startswith("Wait until")


@pytest.mark.parametrize(
    "summary",
    [
        "Sales rose 35% against the comparison.",
        "The change brought 1,000 extra sales.",
        "Sales rose 31.5%.",
    ],
)
def test_numbers_that_are_not_in_the_facts_are_rejected(summary) -> None:
    drafted = DraftedLab.model_validate({**GOOD, "summary": summary})

    assert clean_lab(drafted, facts()) is None


@pytest.mark.parametrize(
    "summary",
    [
        "Sales rose 31%, between 22% and 40% with 90% confidence, or 686 extra items.",
        "The change ran for 28 days, or 4 weeks, after 52 weeks of history.",
        "The comparison tracked closely (93%), and changes above 12% can be seen.",
        "A 10% price cut lifted sales.",
    ],
)
def test_numbers_taken_from_the_facts_are_accepted(summary) -> None:
    drafted = DraftedLab.model_validate({**GOOD, "summary": summary})

    assert clean_lab(drafted, facts()) is not None


def test_warning_values_can_be_written_as_percentages() -> None:
    values = allowed_numbers(
        facts(warnings=[{"code": "noisy", "value": 0.27}, {"code": "short_history", "value": 40}])
    )

    assert {27.0, 40.0} <= values


def test_run_answers_502_when_the_model_fails(models) -> None:
    models.error = RuntimeError("quota")

    assert TestClient(app).post("/prove/lab/run", json=run()).status_code == 502


@pytest.mark.parametrize(
    "change",
    [
        {"affected": ["unknown"]},
        {"affected": []},
        {"comparison": ["running"]},
        {"comparison": ["unknown"]},
        {"start": DAYS},
        {"days": DAYS + 1},
        {"series": [{"key": "running", "name": "Running", "values": [-1.0] * DAYS}]},
    ],
)
def test_check_rejects_bad_requests(change) -> None:
    assert TestClient(app).post("/prove/lab/check", json=data(**change)).status_code == 422


def test_run_needs_a_comparison(models) -> None:
    assert TestClient(app).post("/prove/lab/run", json=run(comparison=[])).status_code == 422
    assert TestClient(app).post("/prove/lab/run", json=run(comparison=None)).status_code == 422
