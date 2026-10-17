import asyncio
import json
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from kairo_agents.agents.ask import api
from kairo_agents.agents.ask.models import AskRequest, AskResponse
from kairo_agents.main import app

BODY = {
    "brandId": "00000000-0000-0000-0000-000000000001",
    "messageId": "00000000-0000-0000-0000-000000000002",
    "token": "A" * 64,
    "question": "Why Pune?",
    "context": {"kind": "campaign", "id": "00000000-0000-0000-0000-000000000003"},
    "now": "2026-10-17T10:00:00Z",
}


def test_tools_only_read_propose_and_send_answer_segments():
    calls = []

    def call(request, body):
        calls.append(body)
        return {"id": "e1"}

    tools = api.tools_for(AskRequest.model_validate(BODY), call)
    assert [tool.__name__ for tool in tools] == ["read_item", "propose_change", "answer_segment"]
    read, propose, answer = tools
    asyncio.run(read("campaign", "room-1"))
    asyncio.run(propose("brief", "room-1", '{"title":"Rain runners"}'))
    asyncio.run(answer("Pune tracked demand closely.", ["e1"]))
    assert [body["operation"] for body in calls] == ["read", "propose", "answer"]
    assert calls[1]["changes"] == {"title": "Rain runners"}
    assert calls[2]["evidenceIds"] == ["e1"]


@pytest.mark.parametrize("kind", ["launch", "approve", "budget", "settings", "stock", "workspace"])
def test_write_tools_reject_unsupported_targets(kind):
    def call(*args):
        pytest.fail("A rejected change must not reach core")

    propose = api.tools_for(AskRequest.model_validate(BODY), call)[1]
    assert asyncio.run(propose(kind, "room-1", '{"status":"approved"}')) == {
        "error": "unsupported_change"
    }


@pytest.mark.parametrize("value", ["invalid", "[]", "null", "{}", "x" * 20001])
def test_bad_proposals_do_not_call_core(value):
    def call(*args):
        pytest.fail("Invalid JSON must not reach core")

    propose = api.tools_for(AskRequest.model_validate(BODY), call)[1]
    assert "error" in asyncio.run(propose("asset", "asset-1", value))


def test_tool_budget_is_per_invocation():
    tools = api.tools_for(AskRequest.model_validate(BODY), lambda *args: {"id": "e1"})
    for _ in range(24):
        assert asyncio.run(tools[0]("workspace")) == {"id": "e1"}
    assert asyncio.run(tools[0]("workspace")) == {"error": "tool_limit"}
    fresh = api.tools_for(AskRequest.model_validate(BODY), lambda *args: {"id": "e1"})
    assert asyncio.run(fresh[0]("workspace")) == {"id": "e1"}


def test_request_limits_and_token_are_private():
    request = AskRequest.model_validate(BODY)
    assert BODY["token"] not in repr(request)
    for patch in [
        {"question": "x" * 4001},
        {"history": [{"role": "user", "text": "x"}] * 21},
        {"callbackUrl": "https://attacker.example"},
        {"brandId": "../another-brand"},
    ]:
        with pytest.raises(ValidationError):
            AskRequest.model_validate({**BODY, **patch})


def test_core_destination_is_server_configured(monkeypatch):
    request = AskRequest.model_validate(BODY)
    monkeypatch.setenv("KAIRO_API_URL", "http://attacker.example")
    with pytest.raises(ValueError, match="HTTPS"):
        api.core_call(request, {})
    monkeypatch.setenv("KAIRO_API_URL", "https://core.example/injected-path")
    with pytest.raises(ValueError, match="origin"):
        api.core_call(request, {})


def test_callbacks_never_forward_invocation_credentials_to_redirects():
    assert (
        api.NoRedirects().redirect_request(None, None, 302, "Redirect", {}, "https://attacker.test")
        is None
    )


def test_runner_rehydrates_history_and_bounds_calls_without_exposing_credentials(monkeypatch):
    seen = {}

    class FakeRunner:
        def __init__(self, **kwargs):
            seen.update(kwargs)

        async def run_async(self, **kwargs):
            seen["run"] = kwargs
            session = await seen["session_service"].get_session(
                app_name=api.APP_NAME, user_id=BODY["brandId"], session_id=BODY["messageId"]
            )
            seen["history"] = session.events
            yield SimpleNamespace(error_code=None)

        async def close(self):
            seen["closed"] = True

    monkeypatch.setattr(api, "Runner", FakeRunner)
    request = AskRequest.model_validate(
        {
            **BODY,
            "history": [
                {"role": "user", "text": "Last month?"},
                {"role": "model", "text": "There was insufficient evidence."},
            ],
        }
    )
    answer = asyncio.run(api.run_turn(request))
    assert answer.prompt_version == api.PROMPT_VERSION
    assert seen["run"]["run_config"].max_llm_calls == 8
    assert len(seen["history"]) == 2
    text = seen["run"]["new_message"].model_dump_json()
    assert BODY["token"] not in text
    assert json.loads(seen["run"]["new_message"].parts[0].text)["currentItem"]["kind"] == "campaign"
    assert seen["agent"].model.client_kwargs["vertexai"] is True
    assert seen["closed"]


def test_endpoint_returns_version_and_hides_failures():
    async def good(request):
        return AskResponse(model="test-pro", prompt_version=api.PROMPT_VERSION)

    async def failed(request):
        raise RuntimeError("secret-token")

    try:
        with TestClient(app) as client:
            app.dependency_overrides[api.runner_source] = lambda: good
            assert client.post("/ask/turn", json=BODY).json() == {
                "model": "test-pro",
                "promptVersion": "ask/1",
            }
            app.dependency_overrides[api.runner_source] = lambda: failed
            response = client.post("/ask/turn", json=BODY)
            assert response.status_code == 502
            assert response.json() == {"error": "ask_failed"}
    finally:
        app.dependency_overrides.clear()
