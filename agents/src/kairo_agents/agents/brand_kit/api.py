import logging
import os
from collections.abc import Callable
from functools import cache
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google import genai
from google.genai import types

from kairo_agents.agents.brand_kit.models import DraftAnswer, DraftRequest, DraftResponse
from kairo_agents.agents.brand_kit.prompt import PROMPT_VERSION, data_block, instructions

DEFAULT_MODEL = "gemini-3.5-flash"
DEFAULT_LOCATION = "global"

logger = logging.getLogger(__name__)
router = APIRouter()


def model_name() -> str:
    return os.environ.get("KAIRO_BRAND_KIT_MODEL") or DEFAULT_MODEL


@cache
def default_client() -> genai.Client:
    return genai.Client(
        vertexai=True,
        project=os.environ.get("GOOGLE_CLOUD_PROJECT"),
        location=os.environ.get("GOOGLE_CLOUD_LOCATION") or DEFAULT_LOCATION,
    )


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


@router.post("/brand-kit/draft", response_model=DraftResponse)
async def draft(request: DraftRequest, clients: ClientSource) -> Any:
    model = model_name()
    try:
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data_block(request),
            config=types.GenerateContentConfig(
                system_instruction=instructions(),
                response_mime_type="application/json",
                response_schema=DraftAnswer,
                temperature=0.2,
            ),
        )
        answer = DraftAnswer.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Drafting the Brand Kit failed.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return DraftResponse(**answer.model_dump(), model=model, prompt_version=PROMPT_VERSION)
