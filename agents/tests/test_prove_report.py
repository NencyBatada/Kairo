import json
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

from kairo_agents.agents.prove.prompt import REPORT_PROMPT_VERSION, report_instructions
from kairo_agents.agents.prove.report import allowed_numbers, clean_report
from kairo_agents.agents.prove.report_api import client_source
from kairo_agents.agents.prove.report_models import DraftedReport, ReportRequest
from kairo_agents.agents.sense.prompt import DATA_END, DATA_START
from kairo_agents.main import app

WORKED = {
    "title": "Monsoon runners",
    "kpi": "orders",
    "currency": "INR",
    "days": 14,
    "plannedDays": 14,
    "outcome": "worked",
    "nextStep": "scale_up",
    "lift": 0.12,
    "low": 0.06,
    "high": 0.18,
    "actual": 1568,
    "expected": 1400,
    "extra": 168,
    "extraLow": 84,
    "extraHigh": 252,
    "spend": 52080,
    "results": 410,
    "costPerResult": 127.02,
    "costPerExtra": 310,
    "costPerExtraLow": 206.67,
    "costPerExtraHigh": 620,
    "targetCost": 400,
    "flags": [{"code": "ended_early", "detail": "The test ended early."}],
    "regions": [
        {
            "name": "Mumbai",
            "test": True,
            "lift": 0.14,
            "low": 0.05,
            "high": 0.23,
            "conclusive": True,
        },
    ],
}

INCONCLUSIVE = {
    **WORKED,
    "outcome": "inconclusive",
    "nextStep": "retest",
    "lift": 0.03,
    "low": -0.05,
    "high": 0.11,
    "costPerExtra": None,
    "costPerExtraLow": None,
    "costPerExtraHigh": None,
    "flags": [],
    "nextTest": {"days": 42, "addRegions": True, "reasons": ["wide_range"]},
}

GOOD = {
    "summary": "Orders in the test regions rose 12% (between 6% and 18%) against the "
    "holdout cities, at INR 310 per extra order. The test ended early, which may have "
    "influenced the result.",
    "nextStepReasoning": "Each extra order cost INR 310, below the INR 400 target, "
    "so scaling up is worthwhile.",
    "inconclusiveReason": "This should be ignored.",
    "nextTestAdvice": "This should be ignored too.",
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


def test_report_makes_one_structured_call_and_drops_inconclusive_text(models, monkeypatch) -> None:
    monkeypatch.setenv("KAIRO_PROVE_MODEL", "gemini-pro-test")

    response = TestClient(app).post("/prove/report", json=WORKED)

    assert response.status_code == 200
    body = response.json()
    assert body["summary"].startswith("Orders in the test regions rose 12%")
    assert body["inconclusiveReason"] is None
    assert body["nextTestAdvice"] is None
    assert (body["model"], body["promptVersion"]) == ("gemini-pro-test", REPORT_PROMPT_VERSION)
    [call] = models.calls
    assert call["model"] == "gemini-pro-test"
    assert call["config"].system_instruction == report_instructions()
    assert call["config"].response_schema is DraftedReport
    assert call["contents"].startswith(DATA_START)
    assert call["contents"].endswith(DATA_END)


def test_report_uses_a_pro_model_by_default(models, monkeypatch) -> None:
    monkeypatch.delenv("KAIRO_PROVE_MODEL", raising=False)

    response = TestClient(app).post("/prove/report", json=WORKED)

    assert response.json()["model"] == "gemini-3.5-pro"


def test_inconclusive_reports_must_explain_themselves(models) -> None:
    models.text = json.dumps(
        {
            "summary": "Orders rose 3%, but the range runs from 5% lower to 11% higher, "
            "so the test can't say.",
            "nextStepReasoning": "A clearer answer needs a longer test.",
            "inconclusiveReason": "The range was too wide for 14 days of data.",
            "nextTestAdvice": "Run the next test for 42 days, or 6 weeks, and add more regions.",
        }
    )

    response = TestClient(app).post("/prove/report", json=INCONCLUSIVE)

    assert response.status_code == 200
    assert response.json()["nextTestAdvice"].startswith("Run the next test for 42 days")

    models.text = json.dumps({**GOOD, "summary": "Orders rose 3%.", "inconclusiveReason": " "})
    assert TestClient(app).post("/prove/report", json=INCONCLUSIVE).status_code == 502


@pytest.mark.parametrize(
    "summary",
    [
        "Orders rose 15% against the holdout cities.",
        "Each extra order cost INR 290.",
        "The campaign brought 1,000 extra orders.",
        "Orders rose 12.4% against the holdout cities.",
    ],
)
def test_numbers_that_are_not_in_the_facts_are_rejected(models, summary) -> None:
    models.text = json.dumps({**GOOD, "summary": summary})

    response = TestClient(app).post("/prove/report", json=WORKED)

    assert response.status_code == 502
    assert response.json() == {"error": "model_failed"}


@pytest.mark.parametrize(
    "summary",
    [
        "Orders rose 12% against the holdout cities, between 6% and 18% with 90% confidence.",
        "The test ran for 14 days, or 2 weeks, and spent INR 52,080.",
        "Mumbai rose 14%, between 5% and 23%, and 168 extra orders came in.",
        "Each extra order cost INR 310, and attributed results cost INR 127.02 each.",
        "Each extra order cost between INR 207 and INR 620.",
    ],
)
def test_numbers_taken_from_the_facts_are_accepted(summary) -> None:
    request = ReportRequest.model_validate(WORKED)
    drafted = DraftedReport.model_validate({**GOOD, "summary": summary})

    assert clean_report(drafted, request) is not None


def test_negative_lifts_can_be_written_as_falls() -> None:
    request = ReportRequest.model_validate(
        {
            **INCONCLUSIVE,
            "outcome": "no_effect",
            "nextStep": "stop",
            "lift": -0.08,
            "low": -0.14,
            "high": -0.02,
        }
    )

    assert {8.0, 14.0, 2.0} <= allowed_numbers(request)


@pytest.mark.parametrize(
    "answer",
    ["not json", json.dumps({"summary": "x"}), json.dumps({**GOOD, "summary": "  "})],
)
def test_report_answers_502_when_the_draft_is_unusable(models, answer) -> None:
    models.text = answer

    assert TestClient(app).post("/prove/report", json=WORKED).status_code == 502


def test_report_answers_502_when_the_model_fails(models) -> None:
    models.error = RuntimeError("quota")

    assert TestClient(app).post("/prove/report", json=WORKED).status_code == 502


@pytest.mark.parametrize(
    "change",
    [
        {"kpi": "revenue"},
        {"outcome": "great"},
        {"plannedDays": 0},
        {"flags": [{"code": "x", "detail": ""}]},
    ],
)
def test_report_rejects_bad_requests(models, change) -> None:
    assert TestClient(app).post("/prove/report", json={**WORKED, **change}).status_code == 422
