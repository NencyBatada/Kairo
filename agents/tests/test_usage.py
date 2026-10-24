import asyncio
import json
from io import BytesIO
from types import SimpleNamespace as NS
from urllib.error import HTTPError
from uuid import uuid4

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from google.genai import types

from kairo_agents import usage
from kairo_agents.telemetry import ObservedClient


def session():
    return usage.UsageSession(str(uuid4()), str(uuid4()), "a" * 64)


def run_in_session(callback):
    async def run():
        token = usage.current.set(session())
        try:
            return await callback()
        finally:
            usage.current.reset(token)

    return asyncio.run(run())


def test_calls_reserve_before_provider_and_report_every_attempt_without_content(monkeypatch):
    events = []

    def core(current, path, body):
        events.append((path, body))
        return {}

    async def provider(**kwargs):
        assert events[-1][0] == "reserve"
        events.append(("provider", {}))
        return NS(
            model_version="gemini-resolved",
            usage_metadata=NS(
                prompt_token_count=10,
                candidates_token_count=3,
                thoughts_token_count=2,
                cached_content_token_count=0,
            ),
        )

    monkeypatch.setattr(usage, "callback", core)

    async def run():
        for _ in range(2):
            await usage.model_call(
                "generate_content",
                provider,
                {"model": "gemini-test", "contents": "private"},
                "global",
            )

    run_in_session(run)
    assert [item[0].split("/")[-1] for item in events] == ["reserve", "provider", "report"] * 2
    assert events[2][1]["thoughtTokens"] == 2
    assert events[0][1]["requestId"] != events[3][1]["requestId"]
    assert "private" not in json.dumps(events)


def test_duplicate_transport_retries_keep_request_id_and_invoke_provider_once(monkeypatch):
    bodies = []
    calls = []

    def core(current, path, body):
        bodies.append((path, body))
        if len(bodies) == 1:
            raise OSError("acknowledgement lost")
        return {}

    async def provider(**kwargs):
        calls.append(1)
        return NS()

    monkeypatch.setattr(usage, "callback", core)
    run_in_session(
        lambda: usage.model_call("generate_content", provider, {"model": "gemini-test"}, "global")
    )
    assert bodies[0] == bodies[1]
    assert calls == [1]
    assert bodies[-1][1]["inputTokens"] is None
    assert bodies[-1][1]["outputTokens"] is None


def test_limit_blocks_provider_and_stops_automatic_retries_even_when_caught(monkeypatch):
    calls = []

    def core(current, path, body):
        calls.append(1)
        raise HTTPError(
            "https://core", 429, "limit", {}, BytesIO(b'{"code":"generation_limit_exceeded"}')
        )

    async def provider(**kwargs):
        raise AssertionError("provider must not run")

    monkeypatch.setattr(usage, "callback", core)

    async def run():
        for _ in range(3):
            with pytest.raises(usage.UsageError, match="generation_limit_exceeded"):
                await usage.model_call(
                    "generate_content", provider, {"model": "gemini-test"}, "global"
                )

    run_in_session(run)
    assert len(calls) == 1


def test_uncertain_failure_is_reported_without_refunding_capacity(monkeypatch):
    bodies = []
    monkeypatch.setattr(
        usage, "callback", lambda current, path, body: bodies.append((path, body)) or {}
    )

    async def provider(**kwargs):
        raise RuntimeError("private provider failure")

    with pytest.raises(RuntimeError):
        run_in_session(
            lambda: usage.model_call(
                "generate_content", provider, {"model": "gemini-test"}, "global"
            )
        )
    assert bodies[-1][1] == {"outcome": "uncertain"}
    assert "private" not in json.dumps(bodies)


def test_images_reserve_all_candidates_and_video_reports_after_poll_completion(monkeypatch):
    bodies = []
    monkeypatch.setattr(
        usage, "callback", lambda current, path, body: bodies.append((path, body)) or {}
    )

    async def image(**kwargs):
        return NS(
            candidates=[NS(content=NS(parts=[NS(inline_data=NS(mime_type="image/png"))]))] * 2
        )

    async def video(**kwargs):
        return NS(name="video-1", done=False)

    async def poll(operation):
        return NS(name=operation.name, done=True, error=None, response=NS(generated_videos=[NS()]))

    async def run():
        await usage.model_call(
            "generate_content",
            image,
            {
                "model": "gemini-image",
                "config": types.GenerateContentConfig(
                    response_modalities=["IMAGE"], candidate_count=2
                ),
            },
            "global",
        )
        operation = await usage.model_call(
            "generate_videos",
            video,
            {
                "model": "veo",
                "config": types.GenerateVideosConfig(duration_seconds=8, number_of_videos=1),
            },
            "us-central1",
        )
        assert bodies[-1][0] == "reserve"
        await usage.UsageOperations(NS(get=poll)).get(operation)

    run_in_session(run)
    assert bodies[0][1]["images"] == 2
    assert bodies[1][1]["generatedImages"] == 2
    assert bodies[2][1]["videoClips"] == 1
    assert bodies[3][1]["videoSeconds"] == 8


def test_failed_reporting_blocks_further_model_work(monkeypatch):
    async def provider(**kwargs):
        return NS()

    def core(current, path, body):
        if path != "reserve":
            raise OSError("outage")
        return {}

    monkeypatch.setattr(usage, "callback", core)

    async def run():
        with pytest.raises(usage.UsageError, match="usage_unavailable"):
            await usage.model_call("generate_content", provider, {"model": "gemini-test"}, "global")
        assert usage.current.get().error == "usage_unavailable"

    run_in_session(run)


def test_middleware_preserves_stable_limit_error_from_caught_provider_exception(monkeypatch):
    def core(current, path, body):
        raise HTTPError(
            "https://core", 429, "limit", {}, BytesIO(b'{"code":"generation_limit_exceeded"}')
        )

    monkeypatch.setattr(usage, "callback", core)
    app = FastAPI()
    app.add_middleware(usage.UsageMiddleware)

    @app.post("/test")
    async def endpoint():
        try:
            await usage.current.get().reserve("gemini", "text", "global", textCalls=1)
        except Exception:
            return {"error": "model_failed"}

    with TestClient(app) as client:
        result = client.post(
            "/test",
            headers={
                "X-Kairo-Usage-Brand": str(uuid4()),
                "X-Kairo-Usage-Invocation": str(uuid4()),
                "X-Kairo-Usage-Token": "a" * 64,
            },
        )
    assert result.status_code == 429
    assert result.json() == {"error": "generation_limit_exceeded"}
    assert usage.current.get() is None


def test_real_client_wrapper_fails_closed_without_invocation():
    async def provider(**kwargs):
        raise AssertionError("provider must not run")

    wrapped = ObservedClient(NS(aio=NS(models=NS(generate_content=provider), operations=NS())))
    with pytest.raises(usage.UsageError, match="usage_unavailable"):
        asyncio.run(wrapped.aio.models.generate_content(model="gemini-test"))


def test_speech_counts_unicode_characters_and_disables_hidden_retries(monkeypatch):
    bodies = []
    monkeypatch.setattr(
        usage, "callback", lambda current, path, body: bodies.append((path, body)) or {}
    )

    async def speak(**kwargs):
        assert kwargs["retry"] is None
        return NS(audio_content=b"invalid")

    text = "नमस्ते 👟"
    run_in_session(
        lambda: usage.UsageSpeech(NS(synthesize_speech=speak)).synthesize_speech(
            input=NS(text=text), voice=NS(name="hi-IN-Standard-A")
        )
    )
    assert bodies[0][1]["speechCharacters"] == len(text)
    assert bodies[1][1]["speechCharacters"] == len(text)
    assert bodies[1][1]["speechSeconds"] is None
    assert text not in json.dumps(bodies)


def test_session_tokens_are_not_printable():
    assert "a" * 64 not in repr(session())


@pytest.mark.parametrize(
    "details,expected",
    [
        (None, None),
        ([], None),
        ([NS(modality="TEXT", token_count=12)], None),
        ([NS(modality="IMAGE", token_count=None)], None),
        ([NS(modality="IMAGE", token_count=0)], 0),
        ([NS(modality="IMAGE", token_count=1290)], 1290),
    ],
)
def test_image_token_details_preserve_unknown_instead_of_pricing_them_as_text(details, expected):
    response = NS(usage_metadata=NS(candidates_tokens_details=details))
    assert usage.token_usage(response)["imageTokens"] == expected


def test_reporting_outage_cannot_downgrade_a_known_generation_limit(monkeypatch):
    def core(current, path, body):
        raise OSError("outage")

    monkeypatch.setattr(usage, "callback", core)

    async def run():
        current = usage.current.get()
        current.error = "generation_limit_exceeded"
        with pytest.raises(usage.UsageError, match="generation_limit_exceeded"):
            await current.report({"requestId": str(uuid4())}, outcome="uncertain")
        assert current.error == "generation_limit_exceeded"

    run_in_session(run)


def test_adk_callbacks_account_for_success_errors_and_interrupted_turns(monkeypatch):
    bodies = []
    monkeypatch.setattr(
        usage, "callback", lambda current, path, body: bodies.append((path, body)) or {}
    )
    telemetry = NS(before=lambda *_: None, after=lambda *_: None, error=lambda *_: None)

    async def run():
        callbacks = usage.UsageModelCallbacks("gemini-test", "global", telemetry)
        await callbacks.before(None, NS())
        await callbacks.after(None, NS(usage_metadata=NS(prompt_token_count=4)))
        await callbacks.before(None, NS())
        await callbacks.error(None, NS(), RuntimeError("private"))
        await callbacks.before(None, NS())
        await callbacks.finish()
        await callbacks.finish()

    run_in_session(run)
    assert len(bodies) == 6
    assert bodies[1][1]["inputTokens"] == 4
    assert bodies[1][1]["outputTokens"] is None
    assert bodies[3][1] == bodies[5][1] == {"outcome": "uncertain"}
    assert "private" not in json.dumps(bodies)
