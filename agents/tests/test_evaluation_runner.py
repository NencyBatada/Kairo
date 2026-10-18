import asyncio
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from kairo_agents.agents.check.models import AssetFindings, CheckResponse, Finding
from kairo_agents.evaluation import runner
from kairo_agents.evaluation.auth import live_credentials
from kairo_agents.evaluation.budget import Budget, Limits
from kairo_agents.evaluation.fixtures import Fixtures
from kairo_agents.evaluation.judge import GoogleJudge
from kairo_agents.evaluation.replay import replay_client

PROJECT = "kairo2"
ACCOUNT = "kairo-evaluations@kairo2.iam.gserviceaccount.com"
PROVIDER = (
    "projects/565205740479/locations/global/"
    "workloadIdentityPools/kairo-evaluations/providers/github"
)


def test_live_missing_credentials_emits_failing_artifact_without_provider_calls(
    tmp_path, monkeypatch
):
    monkeypatch.setenv("KAIRO_EVAL_PROJECT", PROJECT)
    monkeypatch.setenv("KAIRO_EVAL_SERVICE_ACCOUNT", ACCOUNT)
    monkeypatch.setenv("KAIRO_EVAL_PROVIDER", PROVIDER)
    monkeypatch.delenv("GOOGLE_APPLICATION_CREDENTIALS", raising=False)
    assert not asyncio.run(runner.evaluate(tmp_path, True))
    report = json.loads((tmp_path / "report.json").read_text())
    assert not report["liveEvaluationPassed"]
    assert report["failures"] == ["ValueError"]
    assert report["usage"]["requests"] == {"text": 0, "images": 0, "videos": 0}
    assert report["promptManifest"] and report["fixtureManifest"]


@pytest.mark.parametrize(
    "credential_type", ["service_account", "authorized_user", "external_account"]
)
def test_static_keys_and_unexpected_oidc_audiences_are_rejected(
    tmp_path, monkeypatch, credential_type
):
    path = tmp_path / "credentials.json"
    path.write_text(json.dumps({"type": credential_type, "audience": "untrusted"}))
    monkeypatch.setenv("GOOGLE_APPLICATION_CREDENTIALS", str(path))
    with pytest.raises(ValueError, match="trusted_oidc_identity_required"):
        live_credentials(PROJECT, ACCOUNT, PROVIDER)


def test_oidc_configuration_uses_only_the_dedicated_evaluation_identity(tmp_path, monkeypatch):
    path = tmp_path / "credentials.json"
    path.write_text(
        json.dumps(
            {
                "type": "external_account",
                "audience": "//iam.googleapis.com/" + PROVIDER,
                "service_account_impersonation_url": f"https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/{ACCOUNT}:generateAccessToken",
                "token_url": "https://sts.googleapis.com/v1/token",
                "subject_token_type": "urn:ietf:params:oauth:token-type:jwt",
            }
        )
    )
    monkeypatch.setenv("GOOGLE_APPLICATION_CREDENTIALS", str(path))
    identity = object()
    monkeypatch.setattr("google.auth.load_credentials_from_file", lambda *a, **k: (identity, None))
    assert live_credentials(PROJECT, ACCOUNT, PROVIDER) is identity
    with pytest.raises(ValueError, match="dedicated"):
        live_credentials(PROJECT, "kairo-api@kairo2.iam.gserviceaccount.com", PROVIDER)


@pytest.mark.parametrize("score", [None, True, float("nan"), 0, 6, "5"])
def test_missing_invalid_google_judge_results_fail_and_consume_budget(monkeypatch, score):
    budget = Budget()
    judge = GoogleJudge(PROJECT, budget, credentials=object())
    monkeypatch.setattr(judge, "evaluate", lambda *args: {"score": score})
    with pytest.raises(ValueError):
        asyncio.run(judge.score({}, {}))
    assert budget.counts["text"] == 1
    assert budget.calls[0]["outcome"] == "failed"


def test_google_judge_calls_the_official_tool_with_fixture_and_candidate(monkeypatch):
    credentials = SimpleNamespace(valid=True, token="secret-token")
    captured = {}

    def post(url, **kwargs):
        captured.update(url=url, **kwargs)
        return SimpleNamespace(
            raise_for_status=lambda: None, json=lambda: {"pointwiseMetricResult": {"score": 4}}
        )

    monkeypatch.setattr("kairo_agents.evaluation.judge.requests.post", post)
    budget = Budget()
    result = asyncio.run(
        GoogleJudge(PROJECT, budget, credentials=credentials).score(
            {"seed": "fixture"}, {"brief": "candidate"}
        )
    )
    assert result["score"] == 0.8
    assert captured["url"].endswith("/projects/kairo2/locations/us-central1:evaluateInstances")
    payload = captured["json"]["pointwise_metric_input"]
    instance = json.loads(payload["instance"]["json_instance"])
    assert json.loads(instance["context"]) == {"seed": "fixture"}
    assert json.loads(instance["response"]) == {"brief": "candidate"}
    assert captured["allow_redirects"] is False
    assert "secret-token" not in json.dumps(budget.report())
    assert budget.calls[0]["inputTokens"] is None


@pytest.mark.parametrize(
    "case_id", ["brief-en", "brief-hi", "copy-en", "copy-hi", "generated-images", "generated-video"]
)
def test_live_generation_uses_production_functions_and_preserves_versions(tmp_path, case_id):
    fixtures = Fixtures()
    case = next(c for c in fixtures.cases if c["id"] == case_id)
    judge = SimpleNamespace(score=AsyncMock(return_value={"score": 0.8}))
    live = runner.LiveRunner(fixtures, replay_client(fixtures, case), judge, tmp_path)
    result = asyncio.run(live.generation(case))
    assert result["complete"] and result["formatPassed"], result
    assert result["model"] and result["promptVersion"]
    assert bool(judge.score.await_count) == (case["kind"] in ("brief", "copy"))


def test_critical_findings_are_rejected_and_missing_guardian_results_fail(tmp_path):
    fixtures = Fixtures()
    case = next(c for c in fixtures.cases if c.get("critical"))
    live = runner.LiveRunner(fixtures, None, None, tmp_path)
    answer = CheckResponse(
        model="test",
        prompt_version="test/1",
        results=[
            AssetFindings(
                asset_id=case["id"],
                findings=[Finding(rule=case["expectedRule"], message="synthetic planted issue")],
            )
        ],
    )
    live.guardian_results(answer, [case])
    assert live.results[0]["accepted"] is False
    with pytest.raises(ValueError, match="incomplete_guardian"):
        live.guardian_results(answer, [case, {"id": "missing"}])


def test_timeout_and_partial_runs_cannot_count_as_passing(tmp_path, monkeypatch):
    class Client:
        def __init__(self, **kwargs):
            self.aio = SimpleNamespace(models=None, operations=None, aclose=AsyncMock())

        def close(self):
            pass

    async def hang(self):
        self.results.append({"id": "brief-en", "complete": False})
        await asyncio.sleep(10)

    monkeypatch.setattr(runner, "live_credentials", lambda *args: object())
    monkeypatch.setattr(runner.genai, "Client", Client)
    monkeypatch.setattr(runner, "GoogleJudge", lambda *args, **kwargs: None)
    monkeypatch.setattr(runner.LiveRunner, "run", hang)
    assert not asyncio.run(runner.evaluate(tmp_path, True, Limits(seconds=0.05)))
    report = json.loads((tmp_path / "report.json").read_text())
    assert report["failures"] == ["TimeoutError"]
    assert not report["liveEvaluationPassed"]
    assert report["results"] == [{"id": "brief-en", "complete": False}]
