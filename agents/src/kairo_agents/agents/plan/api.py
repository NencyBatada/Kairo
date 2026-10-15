import logging
import os
from collections.abc import Callable
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.genai import types

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.plan.models import DraftedBrief, TriggerBriefRequest, TriggerBriefResponse
from kairo_agents.agents.plan.prompt import TRIGGER_BRIEF_PROMPT_VERSION, trigger_brief_instructions
from kairo_agents.agents.sense.prompt import data_block

DEFAULT_MODEL = "gemini-3.5-flash"
CHANNELS = ("search", "shopping", "demand_gen")
MAX_PRODUCTS = 6
MAX_TITLE = 120
MAX_SENTENCE = 500
MAX_OFFER = 300

logger = logging.getLogger(__name__)
router = APIRouter()


def model_name() -> str:
    return os.environ.get("KAIRO_PLAN_MODEL") or DEFAULT_MODEL


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


@router.post("/plan/trigger-brief", response_model=TriggerBriefResponse)
async def trigger_brief(request: TriggerBriefRequest, clients: ClientSource) -> Any:
    model = model_name()
    try:
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data_block(request),
            config=types.GenerateContentConfig(
                system_instruction=trigger_brief_instructions(),
                response_mime_type="application/json",
                response_schema=DraftedBrief,
                temperature=0.3,
            ),
        )
        drafted = DraftedBrief.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Plan couldn't draft the trigger brief.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    brief = clean_brief(drafted, request)
    if brief is None:
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return TriggerBriefResponse(
        brief=brief, model=model, prompt_version=TRIGGER_BRIEF_PROMPT_VERSION
    )


def clean_brief(drafted: DraftedBrief, request: TriggerBriefRequest) -> DraftedBrief | None:
    title = cut(drafted.title, MAX_TITLE)
    goal = cut(drafted.goal, MAX_SENTENCE)
    audience = cut(drafted.audience, MAX_SENTENCE)
    key_message = cut(drafted.key_message, MAX_SENTENCE)
    timing = cut(drafted.timing, MAX_SENTENCE)
    if not (title and goal and audience and key_message and timing):
        return None
    regions = {region.casefold(): region for region in request.trigger.regions}
    products = {product.id for product in request.products}
    chosen_regions = unique(regions.get(region.strip().casefold()) for region in drafted.regions)
    chosen_products = unique(
        product.strip() for product in drafted.product_ids if product.strip() in products
    )
    channels = unique(
        channel.strip().casefold()
        for channel in drafted.channels
        if channel.strip().casefold() in CHANNELS
    )
    return DraftedBrief(
        title=title,
        goal=goal,
        audience=audience,
        key_message=key_message,
        offer=cut(drafted.offer, MAX_OFFER),
        regions=chosen_regions or list(request.trigger.regions),
        product_ids=chosen_products[:MAX_PRODUCTS],
        channels=channels,
        timing=timing,
    )


def cut(text: str | None, length: int) -> str | None:
    trimmed = (text or "").strip()
    return trimmed[:length] or None


def unique(values: Any) -> list[str]:
    return list(dict.fromkeys(value for value in values if value))
