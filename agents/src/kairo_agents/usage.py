import asyncio
import json
import os
from contextvars import ContextVar
from dataclasses import dataclass, field
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
from uuid import UUID, uuid4

from kairo_agents.telemetry import callback_headers


class UsageError(Exception):
    pass


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def callback(session: "UsageSession", path: str, body: dict[str, Any]) -> Any:
    base = os.environ.get("KAIRO_API_URL", "").rstrip("/")
    address = urlsplit(base)
    if (
        address.scheme not in ("http", "https")
        or not address.netloc
        or address.path
        or address.query
        or address.fragment
        or address.username
    ):
        raise UsageError("usage_unavailable")
    if address.scheme == "http" and address.hostname not in ("localhost", "127.0.0.1"):
        raise UsageError("usage_unavailable")
    headers = {
        **callback_headers(),
        "Content-Type": "application/json",
        "X-Kairo-Request": "1",
        "X-Kairo-Usage-Token": session.token,
    }
    if audience := os.environ.get("KAIRO_API_AUDIENCE"):
        from google.auth.transport.requests import Request as GoogleRequest
        from google.oauth2.id_token import fetch_id_token

        headers["Authorization"] = f"Bearer {fetch_id_token(GoogleRequest(), audience)}"
    endpoint = f"/api/brands/{session.brand}/operations/invocations/{session.invocation}/{path}"
    request = Request(base + endpoint, json.dumps(body).encode(), headers, method="POST")
    with build_opener(NoRedirects()).open(request, timeout=20) as response:
        return json.load(response)


@dataclass
class UsageSession:
    brand: str = ""
    invocation: str = ""
    token: str = field(default="", repr=False)
    error: str | None = None
    pending: dict[str, tuple[dict[str, Any], Any]] = field(default_factory=dict, repr=False)

    async def send(self, path: str, body: dict[str, Any]) -> Any:
        if not self.brand or not self.invocation or len(self.token) != 64:
            self.error = self.error or "usage_unavailable"
            raise UsageError(self.error)
        for attempt in range(3):
            try:
                return await asyncio.to_thread(callback, self, path, body)
            except HTTPError as error:
                if error.code == 429:
                    try:
                        code = json.load(error).get("code")
                    except (ValueError, AttributeError):
                        code = None
                    if code == "generation_limit_exceeded":
                        self.error = code
                        raise UsageError(code) from None
                if error.code not in (408, 429, 500, 502, 503, 504):
                    break
            except (URLError, OSError, ValueError):
                pass
            except Exception:
                break
            if attempt < 2:
                await asyncio.sleep(0.2 * (attempt + 1))
        self.error = self.error or "usage_unavailable"
        raise UsageError(self.error)

    async def reserve(
        self, model: str, operation: str, region: str, **values: Any
    ) -> dict[str, Any]:
        if self.error:
            raise UsageError(self.error)
        request = {
            "requestId": str(uuid4()),
            "model": model,
            "operation": operation,
            "region": region,
            **values,
        }
        await self.send("reserve", request)
        return request

    async def report(self, request: dict[str, Any], **values: Any) -> None:
        await self.send(f"calls/{request['requestId']}/report", values)


current: ContextVar[UsageSession | None] = ContextVar("kairo_usage", default=None)


def count(value: Any, name: str) -> int | None:
    item = getattr(value, name, None)
    return item if type(item) is int and item >= 0 else None


def token_usage(response: Any) -> dict[str, Any]:
    usage = getattr(response, "usage_metadata", None)
    details = getattr(usage, "candidates_tokens_details", None)
    image_tokens = None
    if details is not None:
        counts = [
            count(part, "token_count")
            for part in details
            if str(getattr(part, "modality", "")).split(".")[-1] == "IMAGE"
        ]
        if counts and all(value is not None for value in counts):
            image_tokens = sum(counts)
    return {
        "resolvedModel": getattr(response, "model_version", None),
        "inputTokens": count(usage, "prompt_token_count"),
        "outputTokens": count(usage, "candidates_token_count"),
        "thoughtTokens": count(usage, "thoughts_token_count"),
        "cachedTokens": count(usage, "cached_content_token_count"),
        "imageTokens": image_tokens,
    }


async def model_call(operation: str, call: Any, kwargs: dict[str, Any], region: str) -> Any:
    session = current.get()
    if session is None:
        raise UsageError("usage_unavailable")
    config = kwargs.get("config")

    def value(name, default=None):
        return (
            config.get(name, default)
            if isinstance(config, dict)
            else getattr(config, name, default)
        )

    modalities = [str(item).split(".")[-1] for item in value("response_modalities", []) or []]
    kind = (
        "video" if operation == "generate_videos" else "image" if "IMAGE" in modalities else "text"
    )
    dimensions: dict[str, Any] = (
        {"textCalls": 1}
        if kind == "text"
        else {"images": value("candidate_count", 1) or 1}
        if kind == "image"
        else {
            "videoClips": value("number_of_videos", 1) or 1,
            "requestedVideoSeconds": value("duration_seconds"),
            "videoAudio": value("generate_audio"),
            "videoResolution": value("resolution"),
        }
    )
    request = await session.reserve(kwargs["model"], kind, region, **dimensions)
    try:
        response = await call(**kwargs)
    except BaseException:
        await session.report(request, outcome="uncertain")
        raise
    if kind == "video":
        name = getattr(response, "name", None)
        if not name:
            await session.report(request, outcome="uncertain")
        elif getattr(response, "done", False):
            await report_video(session, request, response)
        else:
            session.pending[name] = (request, response)
    else:
        values = token_usage(response)
        if kind == "image":
            values["generatedImages"] = sum(
                1
                for candidate in getattr(response, "candidates", None) or []
                for part in getattr(getattr(candidate, "content", None), "parts", None) or []
                if getattr(getattr(part, "inline_data", None), "mime_type", "").startswith("image/")
            )
        await session.report(request, outcome="succeeded", **values)
    return response


async def report_video(session: UsageSession, request: dict[str, Any], operation: Any) -> None:
    result = getattr(operation, "response", None)
    videos = getattr(result, "generated_videos", None)
    succeeded = not getattr(operation, "error", None) and bool(videos)
    seconds = request["requestedVideoSeconds"] * len(videos) if succeeded else None
    await session.report(
        request, outcome="succeeded" if succeeded else "uncertain", videoSeconds=seconds
    )


class UsageOperations:
    def __init__(self, operations: Any):
        self.operations = operations

    def __getattr__(self, name: str) -> Any:
        return getattr(self.operations, name)

    async def get(self, operation: Any, **kwargs: Any) -> Any:
        result = await self.operations.get(operation, **kwargs)
        session = current.get()
        name = getattr(operation, "name", None)
        if session and getattr(result, "done", False) and name in session.pending:
            request, _ = session.pending.pop(name)
            await report_video(session, request, result)
        return result


class UsageSpeech:
    def __init__(self, speech: Any):
        self.speech = speech

    async def synthesize_speech(self, **kwargs: Any) -> Any:
        session = current.get()
        if session is None:
            raise UsageError("usage_unavailable")
        characters = len(kwargs["input"].text)
        request = await session.reserve(
            kwargs["voice"].name, "speech", "global", speechCharacters=characters
        )
        try:
            response = await self.speech.synthesize_speech(**kwargs, retry=None)
        except BaseException:
            await session.report(request, outcome="uncertain", speechCharacters=characters)
            raise
        from kairo_agents.media.audio import read_wav

        try:
            seconds = read_wav(response.audio_content).seconds
        except Exception:
            seconds = None
        await session.report(
            request, outcome="succeeded", speechCharacters=characters, speechSeconds=seconds
        )
        return response


class UsageModelCallbacks:
    def __init__(self, model: str, region: str, telemetry: Any):
        self.model = model
        self.region = region
        self.telemetry = telemetry
        self.request: dict[str, Any] | None = None

    async def before(self, callback_context: Any, llm_request: Any) -> None:
        session = current.get()
        if session is None:
            raise UsageError("usage_unavailable")
        self.request = await session.reserve(self.model, "text", self.region, textCalls=1)
        self.telemetry.before(callback_context, llm_request)

    async def after(self, callback_context: Any, llm_response: Any) -> None:
        session = current.get()
        if session and self.request:
            await session.report(self.request, outcome="succeeded", **token_usage(llm_response))
            self.request = None
        self.telemetry.after(callback_context, llm_response)

    async def error(self, callback_context: Any, llm_request: Any, error: Exception) -> None:
        await self.finish()
        self.telemetry.error(callback_context, llm_request, error)

    async def finish(self) -> None:
        session = current.get()
        if session and self.request:
            request, self.request = self.request, None
            await session.report(request, outcome="uncertain")


class UsageMiddleware:
    def __init__(self, app: Any):
        self.app = app

    async def __call__(self, scope: Any, receive: Any, send: Any) -> None:
        if scope["type"] != "http" or scope["path"] == "/health":
            await self.app(scope, receive, send)
            return
        headers = {
            key.decode("latin-1").lower(): value.decode("latin-1")
            for key, value in scope["headers"]
        }
        session = UsageSession()
        try:
            session.brand = str(UUID(headers.get("x-kairo-usage-brand", "")))
            session.invocation = str(UUID(headers.get("x-kairo-usage-invocation", "")))
            session.token = headers.get("x-kairo-usage-token", "")
        except ValueError:
            pass
        token = current.set(session)
        replaced = False

        async def respond(message: Any) -> None:
            nonlocal replaced
            if message["type"] == "http.response.start" and session.error:
                replaced = True
                body = json.dumps({"error": session.error}).encode()
                await send(
                    {
                        "type": "http.response.start",
                        "status": 429 if session.error == "generation_limit_exceeded" else 503,
                        "headers": [
                            (b"content-type", b"application/json"),
                            (b"content-length", str(len(body)).encode()),
                        ],
                    }
                )
                await send({"type": "http.response.body", "body": body})
            elif not replaced:
                await send(message)

        try:
            await self.app(scope, receive, respond)
        finally:
            current.reset(token)
