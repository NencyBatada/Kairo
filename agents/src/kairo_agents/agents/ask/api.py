import asyncio
import json
import logging
import os
from collections.abc import Callable
from importlib import resources
from typing import Annotated, Any
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.adk.agents import LlmAgent
from google.adk.agents.run_config import RunConfig
from google.adk.events import Event
from google.adk.models.google_llm import Gemini
from google.adk.runners import Runner
from google.adk.sessions import InMemorySessionService
from google.genai import types

from kairo_agents.agents.ask.models import AskRequest, AskResponse
from kairo_agents.agents.ask.telemetry import AskModelTelemetry
from kairo_agents.telemetry import callback_headers

PROMPT_VERSION = "ask/1"
APP_NAME = "kairo_ask"
logger = logging.getLogger(__name__)
router = APIRouter()


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def instructions() -> str:
    return (resources.files("kairo_agents.agents.ask") / "prompts" / "ask_v1.md").read_text(
        encoding="utf-8"
    )


def core_call(request: AskRequest, body: dict[str, Any]) -> Any:
    base = os.environ.get("KAIRO_API_URL", "").rstrip("/")
    address = urlsplit(base)
    if (
        address.scheme not in ("https", "http")
        or not address.netloc
        or address.path
        or address.query
        or address.fragment
        or address.username
    ):
        raise ValueError("KAIRO_API_URL must be the core service origin")
    if address.scheme == "http" and address.hostname not in ("localhost", "127.0.0.1"):
        raise ValueError("Core callbacks require HTTPS outside local development")
    headers = {
        **callback_headers(),
        "Content-Type": "application/json",
        "X-Kairo-Request": "1",
        "X-Kairo-Ask-Token": request.token,
    }
    if audience := os.environ.get("KAIRO_API_AUDIENCE"):
        from google.auth.transport.requests import Request as GoogleRequest
        from google.oauth2.id_token import fetch_id_token

        headers["Authorization"] = f"Bearer {fetch_id_token(GoogleRequest(), audience)}"
    path = f"/api/brands/{request.brand_id}/ask/invocations/{request.message_id}/tools"
    call = Request(base + path, json.dumps(body).encode(), headers, method="POST")
    try:
        with build_opener(NoRedirects()).open(call, timeout=30) as response:
            return json.load(response)
    except HTTPError as error:
        try:
            message = str(json.load(error).get("title", "The tool is unavailable."))[:500]
        except (ValueError, AttributeError):
            message = "The tool is unavailable."
        return {"error": "tool_rejected", "status": error.code, "message": message}


def tools_for(
    request: AskRequest, call: Callable[..., Any] = core_call
) -> list[Callable[..., Any]]:
    count = 0

    async def invoke(body: dict[str, Any]) -> Any:
        nonlocal count
        count += 1
        if count > 24:
            return {"error": "tool_limit"}
        return await asyncio.to_thread(call, request, body)

    async def read_item(kind: str, item_id: str = "", campaign_id: str = "") -> Any:
        """Read a workspace item and obtain a citation ID and its source data."""
        return await invoke(
            {
                "operation": "read",
                "target": {"kind": kind, "id": item_id or None, "campaignId": campaign_id or None},
            }
        )

    async def propose_change(
        kind: str, item_id: str, changes_json: str, campaign_id: str = ""
    ) -> Any:
        """Queue a change preview for the user to inspect and confirm in the application."""
        if kind not in ("brief", "asset", "brand_kit", "catalogue", "trigger_rule", "brand_memory"):
            return {"error": "unsupported_change"}
        if len(changes_json) > 20000:
            return {"error": "change_too_large"}
        try:
            changes = json.loads(changes_json)
        except ValueError:
            return {"error": "invalid_change"}
        if not isinstance(changes, dict) or not changes:
            return {"error": "invalid_change"}
        return await invoke(
            {
                "operation": "propose",
                "target": {"kind": kind, "id": item_id or None, "campaignId": campaign_id or None},
                "changes": changes,
            }
        )

    async def answer_segment(text: str, evidence_ids: list[str]) -> Any:
        """Send a short answer paragraph with citation IDs returned by read_item."""
        return await invoke({"operation": "answer", "text": text, "evidenceIds": evidence_ids})

    return [read_item, propose_change, answer_segment]


async def run_turn(request: AskRequest) -> AskResponse:
    model = os.environ.get("KAIRO_ASK_MODEL") or "gemini-3.5-pro"
    telemetry = AskModelTelemetry(model)
    agent = LlmAgent(
        name=APP_NAME,
        model=Gemini(
            model=model,
            client_kwargs={
                "vertexai": True,
                "project": os.environ.get("GOOGLE_CLOUD_PROJECT"),
                "location": os.environ.get("GOOGLE_CLOUD_LOCATION") or "global",
            },
        ),
        instruction=instructions(),
        tools=tools_for(request),
        before_model_callback=telemetry.before,
        after_model_callback=telemetry.after,
        on_model_error_callback=telemetry.error,
        generate_content_config=types.GenerateContentConfig(
            temperature=0.2, max_output_tokens=8192
        ),
    )
    sessions = InMemorySessionService()
    session = await sessions.create_session(
        app_name=APP_NAME, user_id=request.brand_id, session_id=request.message_id
    )
    for turn in request.history:
        await sessions.append_event(
            session,
            Event(
                author="user" if turn.role == "user" else APP_NAME,
                content=types.Content(role=turn.role, parts=[types.Part(text=turn.text)]),
            ),
        )
    runner = Runner(app_name=APP_NAME, agent=agent, session_service=sessions)
    context = json.dumps(
        {"currentItem": request.context.model_dump(by_alias=True), "now": request.now}
    )
    try:
        async with asyncio.timeout(280):
            async for event in runner.run_async(
                user_id=request.brand_id,
                session_id=request.message_id,
                new_message=types.Content(
                    role="user", parts=[types.Part(text=context), types.Part(text=request.question)]
                ),
                run_config=RunConfig(max_llm_calls=8),
            ):
                if event.error_code:
                    raise RuntimeError("Ask agent failed")
    finally:
        telemetry.finish("failed")
        await runner.close()
    return AskResponse(model=model, prompt_version=PROMPT_VERSION)


def runner_source() -> Callable[..., Any]:
    return run_turn


RunnerSource = Annotated[Callable[..., Any], Depends(runner_source)]


@router.post("/ask/turn", response_model=AskResponse)
async def ask_turn(request: AskRequest, run: RunnerSource) -> Any:
    try:
        return await run(request)
    except Exception:
        logger.error("Ask invocation failed")
        return JSONResponse(status_code=502, content={"error": "ask_failed"})
