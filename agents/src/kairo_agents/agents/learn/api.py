import logging
import os
from collections.abc import Callable
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.genai import types

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.learn.lessons import clean_lessons, proven
from kairo_agents.agents.learn.models import DraftLessons, LearnRequest, LearnResponse
from kairo_agents.agents.learn.prompt import LESSONS_PROMPT_VERSION, lessons_instructions
from kairo_agents.agents.plan.api import usage_of
from kairo_agents.agents.sense.prompt import data_block

DEFAULT_MODEL = "gemini-3.5-flash"

logger = logging.getLogger(__name__)
router = APIRouter()


def model_name() -> str:
    return os.environ.get("KAIRO_LEARN_MODEL") or DEFAULT_MODEL


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


@router.post("/learn/lessons", response_model=LearnResponse)
async def lessons(request: LearnRequest, clients: ClientSource) -> Any:
    if not proven(request):
        return LearnResponse(lessons=[], prompt_version=LESSONS_PROMPT_VERSION)
    model = model_name()
    try:
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data_block(request),
            config=types.GenerateContentConfig(
                system_instruction=lessons_instructions(),
                response_mime_type="application/json",
                response_schema=DraftLessons,
                temperature=0.2,
            ),
        )
        drafted = DraftLessons.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Learn couldn't draft the lessons.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return LearnResponse(
        lessons=clean_lessons(drafted.lessons, request),
        model=model,
        prompt_version=LESSONS_PROMPT_VERSION,
        usage=usage_of(response),
    )
