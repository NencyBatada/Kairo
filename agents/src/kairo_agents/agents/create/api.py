import asyncio
import logging
import os
from collections.abc import Callable
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.genai import types
from pydantic import BaseModel, ValidationError

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.create.copy import (
    Cleaned,
    clean_demand_gen,
    clean_revision,
    clean_search,
    clean_titles,
    guard_for,
)
from kairo_agents.agents.create.languages import guidance, language_data
from kairo_agents.agents.create.models import (
    CopyInstructionRequest,
    CopyInstructionResponse,
    CopyRequest,
    CopyResponse,
    CopySet,
    DraftDemandGen,
    DraftRevision,
    DraftSearch,
    DraftTitles,
)
from kairo_agents.agents.create.prompt import (
    COPY_INSTRUCTION_PROMPT_VERSION,
    COPY_PROMPT_VERSION,
    copy_instruction_instructions,
    demand_gen_instructions,
    search_instructions,
    titles_instructions,
)
from kairo_agents.agents.plan.api import usage_of
from kairo_agents.agents.plan.models import Usage
from kairo_agents.agents.sense.prompt import data_block

DEFAULT_MODEL = "gemini-3.5-flash"
COPY_TEMPERATURE = 0.7
TITLE_TEMPERATURE = 0.3
INSTRUCTION_TEMPERATURE = 0.5
MAX_SUMMARY = 300

logger = logging.getLogger(__name__)
router = APIRouter()


class ModelFailed(Exception):
    pass


def model_name() -> str:
    return os.environ.get("KAIRO_CREATE_MODEL") or DEFAULT_MODEL


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


async def generate(
    clients: Callable[[], Any],
    model: str,
    instructions: str,
    schema: type[BaseModel],
    data: str,
    temperature: float,
    clean: Callable[[Any], Cleaned],
    language: str = "en",
) -> tuple[Any, list[Usage | None]]:
    usages: list[Usage | None] = []
    problems: list[str] = []
    for attempt in range(2):
        system = instructions
        if attempt:
            notes = "\n".join(
                f"- {problem}" for problem in [*problems, guidance(language)] if problem
            )
            system = f"{instructions}\n\nYour last answer was rejected because:\n{notes}\n\nFix it."
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data,
            config=types.GenerateContentConfig(
                system_instruction=system,
                response_mime_type="application/json",
                response_schema=schema,
                temperature=temperature,
            ),
        )
        usages.append(usage_of(response))
        try:
            draft = schema.model_validate_json(response.text or "")
        except ValidationError:
            problems = ["Return JSON that matches the schema."]
            continue
        cleaned = clean(draft)
        if cleaned.value is not None:
            return cleaned.value, usages
        problems = cleaned.problems
    raise ModelFailed("; ".join(problems))


def copy_data(request: CopyRequest | CopyInstructionRequest) -> str:
    return data_block({**request.model_dump(by_alias=True), **language_data(request.language)})


def total(usages: list[Usage | None]) -> Usage | None:
    known = [usage for usage in usages if usage is not None]
    if not known:
        return None
    inputs = [usage.input_tokens for usage in known if usage.input_tokens is not None]
    outputs = [usage.output_tokens for usage in known if usage.output_tokens is not None]
    return Usage(
        input_tokens=sum(inputs) if inputs else None,
        output_tokens=sum(outputs) if outputs else None,
    )


@router.post("/create/copy", response_model=CopyResponse)
async def copy(request: CopyRequest, clients: ClientSource) -> Any:
    model = model_name()
    guard = guard_for(
        request.brief, request.products, request.brand_kit, request.language, request.brand
    )
    data = copy_data(request)
    jobs: dict[str, Any] = {}
    if "search" in request.campaign_types:
        jobs["search"] = generate(
            clients,
            model,
            search_instructions(),
            DraftSearch,
            data,
            COPY_TEMPERATURE,
            lambda draft: clean_search(draft, request.brief, request.products, guard),
            request.language,
        )
    if "demand_gen" in request.campaign_types:
        jobs["demand_gen"] = generate(
            clients,
            model,
            demand_gen_instructions(),
            DraftDemandGen,
            data,
            COPY_TEMPERATURE,
            lambda draft: clean_demand_gen(
                draft, request.brand, request.brief, request.products, guard
            ),
            request.language,
        )
    if "shopping" in request.campaign_types and request.products:
        jobs["shopping"] = generate(
            clients,
            model,
            titles_instructions(),
            DraftTitles,
            data,
            TITLE_TEMPERATURE,
            lambda draft: clean_titles(draft, request.products, guard),
        )
    try:
        results = dict(zip(jobs, await asyncio.gather(*jobs.values()), strict=True))
    except Exception:
        logger.exception("Create couldn't write the copy.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    usages = [usage for _, used in results.values() for usage in used]
    return CopyResponse(
        assets=CopySet(
            ad_groups=results["search"][0] if "search" in results else [],
            demand_gen=results["demand_gen"][0] if "demand_gen" in results else None,
            shopping_titles=results["shopping"][0] if "shopping" in results else [],
        ),
        model=model,
        prompt_version=COPY_PROMPT_VERSION,
        usage=total(usages),
    )


@router.post("/create/copy-instruction", response_model=CopyInstructionResponse)
async def copy_instruction(request: CopyInstructionRequest, clients: ClientSource) -> Any:
    model = model_name()
    guard = guard_for(
        request.brief, request.products, request.brand_kit, request.language, request.brand
    )
    summary: list[str] = []

    def clean(draft: DraftRevision) -> Cleaned:
        summary[:] = [" ".join(draft.summary.split())[:MAX_SUMMARY]]
        return clean_revision(
            request.kind,
            draft,
            request.current,
            request.brand,
            request.brief,
            request.products,
            guard,
        )

    try:
        asset, usages = await generate(
            clients,
            model,
            copy_instruction_instructions(),
            DraftRevision,
            copy_data(request),
            INSTRUCTION_TEMPERATURE,
            clean,
            request.language,
        )
    except Exception:
        logger.exception("Create couldn't follow the copy instruction.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return CopyInstructionResponse(
        asset=asset,
        summary=(summary[0] if summary else "") or "Rewrote the asset.",
        model=model,
        prompt_version=COPY_INSTRUCTION_PROMPT_VERSION,
        usage=total(usages),
    )
