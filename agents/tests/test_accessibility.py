import asyncio
import base64
import json
import sys
import wave
from contextvars import ContextVar
from io import BytesIO
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest
from fastapi import HTTPException

from kairo_agents.media.accessibility import (
    Alternatives,
    DescribeRequest,
    DraftRequest,
    draft_alternatives,
    duration,
    render_description,
    require_accounting,
)
from kairo_agents.media.ffmpeg import run_ffmpeg


def request(**values):
    return DraftRequest(
        data=base64.b64encode(b"final media bytes").decode(),
        contentType="image/png",
        language="en",
        **values,
    )


def test_drafting_disabled_without_verified_accounting(monkeypatch):
    monkeypatch.delenv("KAIRO_ACCESSIBILITY_AI_DRAFTING_ENABLED", raising=False)
    with pytest.raises(HTTPException) as error:
        require_accounting()
    assert error.value.status_code == 503
    assert error.value.detail == "accessibility_drafting_unavailable"


def test_drafting_requires_an_invocation_even_when_enabled(monkeypatch):
    monkeypatch.setenv("KAIRO_ACCESSIBILITY_AI_DRAFTING_ENABLED", "true")
    current = ContextVar("test_usage", default=None)
    monkeypatch.setitem(sys.modules, "kairo_agents.usage", SimpleNamespace(current=current))
    with pytest.raises(HTTPException) as error:
        require_accounting()
    assert error.value.detail == "usage_unavailable"
    token = current.set(SimpleNamespace(brand="brand", invocation="invocation", token="x" * 64))
    try:
        require_accounting()
    finally:
        current.reset(token)


def test_drafts_use_actual_media_and_never_claim_review():
    generate = AsyncMock(
        return_value=SimpleNamespace(
            text=json.dumps(
                {"language": "en", "altText": "A red shoe.", "audioConveysVisuals": True}
            )
        )
    )
    client = SimpleNamespace(aio=SimpleNamespace(models=SimpleNamespace(generate_content=generate)))
    result = asyncio.run(draft_alternatives(request(), lambda: client))
    assert result.alt_text == "A red shoe."
    assert result.audio_conveys_visuals is False
    args = generate.call_args.kwargs
    assert args["contents"][0].inline_data.data == b"final media bytes"
    assert "untrusted data" in args["config"].system_instruction
    assert "tools" not in args or not args["tools"]


@pytest.mark.parametrize("response", ["not JSON", '{"language":"hi"}'])
def test_incomplete_or_wrong_language_runs_are_failures(response):
    generate = AsyncMock(return_value=SimpleNamespace(text=response))
    client = SimpleNamespace(aio=SimpleNamespace(models=SimpleNamespace(generate_content=generate)))
    with pytest.raises(HTTPException) as error:
        asyncio.run(draft_alternatives(request(), lambda: client))
    assert error.value.detail == "accessibility_draft_failed"
    assert generate.await_count == 1


def test_generation_caps_stop_without_retries():
    generate = AsyncMock(side_effect=RuntimeError("generation_limit_exceeded"))
    client = SimpleNamespace(aio=SimpleNamespace(models=SimpleNamespace(generate_content=generate)))
    with pytest.raises(HTTPException) as error:
        asyncio.run(draft_alternatives(request(), lambda: client))
    assert (error.value.status_code, error.value.detail) == (429, "generation_limit_exceeded")
    assert generate.await_count == 1


def test_bad_media_never_reaches_the_provider():
    generate = AsyncMock()
    client = SimpleNamespace(aio=SimpleNamespace(models=SimpleNamespace(generate_content=generate)))
    bad = request().model_copy(update={"data": "bad base64!"})
    with pytest.raises(HTTPException) as error:
        asyncio.run(draft_alternatives(bad, lambda: client))
    assert error.value.status_code == 400
    generate.assert_not_called()


def test_overlapping_captions_are_rejected():
    with pytest.raises(ValueError):
        Alternatives.model_validate(
            {
                "language": "en",
                "captions": [
                    {"start": 0, "end": 2, "text": "First"},
                    {"start": 1, "end": 3, "text": "Second"},
                ],
            }
        )


def narration(seconds=0.25):
    output = BytesIO()
    with wave.open(output, "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(24000)
        writer.writeframes(b"\0\0" * round(24000 * seconds))
    return output.getvalue()


def source_video(tmp_path):
    asyncio.run(
        run_ffmpeg(
            [
                "-f",
                "lavfi",
                "-i",
                "color=c=red:s=64x64:d=3:r=10",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440:duration=3",
                "-shortest",
                "-c:v",
                "libx264",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "source.mp4",
            ],
            tmp_path,
        )
    )
    return (tmp_path / "source.mp4").read_bytes()


def described_request(data, **overrides):
    return DescribeRequest.model_validate(
        {
            "data": base64.b64encode(data).decode(),
            "language": "en",
            "descriptionCues": [{"start": 1, "end": 2, "text": "A red shoe turns."}],
            "captions": [{"start": 0, "end": 0.5, "text": "Original speech."}],
            **overrides,
        }
    )


def test_described_video_preserves_source_and_duration(tmp_path):
    original = source_video(tmp_path)
    speech = SimpleNamespace(
        synthesize_speech=AsyncMock(return_value=SimpleNamespace(audio_content=narration()))
    )
    result = asyncio.run(render_description(described_request(original), speech))
    assert result[4:8] == b"ftyp"
    assert (tmp_path / "source.mp4").read_bytes() == original
    (tmp_path / "described.mp4").write_bytes(result)
    assert abs(duration(tmp_path / "described.mp4") - 3) < 0.1
    assert speech.synthesize_speech.await_count == 1


def test_spoken_descriptions_cannot_overlap_captioned_sound(tmp_path):
    speech = SimpleNamespace(synthesize_speech=AsyncMock())
    request = described_request(
        source_video(tmp_path), descriptionCues=[{"start": 0, "end": 1, "text": "A shoe."}]
    )
    with pytest.raises(HTTPException) as error:
        asyncio.run(render_description(request, speech))
    assert error.value.detail == "description_needs_quiet_gap"
    speech.synthesize_speech.assert_not_called()


def test_overlong_narration_is_rejected_without_retry(tmp_path):
    speech = SimpleNamespace(
        synthesize_speech=AsyncMock(return_value=SimpleNamespace(audio_content=narration(2)))
    )
    with pytest.raises(HTTPException) as error:
        asyncio.run(render_description(described_request(source_video(tmp_path)), speech))
    assert error.value.detail == "description_window_too_short"
    assert speech.synthesize_speech.await_count == 1
