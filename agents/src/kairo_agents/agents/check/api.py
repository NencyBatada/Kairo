import logging
import os
from collections.abc import Callable
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.genai import types
from pydantic import BaseModel, ValidationError

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.check.findings import (
    ACCURACY,
    Item,
    coded,
    criteria,
    criteria_data,
    enabled,
    fields_of,
    judged,
    merged,
    text_items,
)
from kairo_agents.agents.check.models import (
    AssetFindings,
    CheckRequestBase,
    CheckResponse,
    CopyCheckRequest,
    DraftImageReview,
    DraftReview,
    DraftVideoReview,
    Finding,
    ImageCheckRequest,
    VideoCheckRequest,
)
from kairo_agents.agents.check.prompt import (
    COPY_PROMPT_VERSION,
    IMAGE_PROMPT_VERSION,
    VIDEO_PROMPT_VERSION,
    copy_instructions,
    image_instructions,
    video_instructions,
)
from kairo_agents.agents.create.api import total
from kairo_agents.agents.create.copy import guard_for, tidy
from kairo_agents.agents.create.languages import language_data
from kairo_agents.agents.create.media_models import Photo, ReferenceProduct
from kairo_agents.agents.plan.api import usage_of
from kairo_agents.agents.plan.models import Usage
from kairo_agents.agents.sense.prompt import data_block
from kairo_agents.media.ffmpeg import FfmpegError
from kairo_agents.media.frames import SampledFrame, sample_frames

DEFAULT_MODEL = "gemini-3.5-pro"
TEMPERATURE = 0.2
MAX_ON_IMAGE_TEXT = 10
MAX_TEXT = 200
RETRY_NOTE = (
    "\n\nYour last answer wasn't valid JSON for the schema. Answer again with JSON that matches "
    "the schema."
)

logger = logging.getLogger(__name__)
router = APIRouter()


class ModelFailed(Exception):
    pass


def model_name() -> str:
    return os.environ.get("KAIRO_CHECK_MODEL") or DEFAULT_MODEL


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


async def review[T: BaseModel](
    clients: Callable[[], Any], model: str, instructions: str, contents: Any, schema: type[T]
) -> tuple[T, list[Usage | None]]:
    usages: list[Usage | None] = []
    for attempt in range(2):
        response = await clients().aio.models.generate_content(
            model=model,
            contents=contents,
            config=types.GenerateContentConfig(
                system_instruction=instructions + (RETRY_NOTE if attempt else ""),
                response_mime_type="application/json",
                response_schema=schema,
                temperature=TEMPERATURE,
            ),
        )
        usages.append(usage_of(response))
        try:
            return schema.model_validate_json(response.text or ""), usages
        except ValidationError:
            continue
    raise ModelFailed("The review didn't match the schema.")


def shared_data(request: CheckRequestBase, chosen: list[Any]) -> dict[str, Any]:
    return {
        **request.model_dump(
            by_alias=True,
            include={"brand", "language", "brief", "products", "brand_kit"},
        ),
        **language_data(request.language),
        "rules": criteria_data(chosen),
    }


def references_data(references: list[ReferenceProduct]) -> list[dict[str, str]]:
    return [
        {"id": product.id, "title": product.title, "description": product.description}
        for product in references
    ]


def photo_part(photo: Photo) -> types.Part:
    return types.Part.from_bytes(data=photo.data, mime_type=photo.mime_type)


def reference_parts(references: list[ReferenceProduct]) -> list[types.Part]:
    parts: list[types.Part] = []
    for product in references:
        parts.append(
            types.Part.from_text(text=f"Catalogue photos of product {product.id}: {product.title}")
        )
        parts += [photo_part(photo) for photo in product.photos]
    return parts


def failed() -> JSONResponse:
    return JSONResponse(status_code=502, content={"error": "model_failed"})


def respond(
    results: list[AssetFindings], model: str, version: str, usages: list[Usage | None]
) -> CheckResponse:
    return CheckResponse(results=results, model=model, prompt_version=version, usage=total(usages))


@router.post("/check/copy", response_model=CheckResponse)
async def check_copy(request: CopyCheckRequest, clients: ClientSource) -> Any:
    model = model_name()
    guard = guard_for(
        request.brief, request.products, request.brand_kit, request.language, request.brand
    )
    rules = enabled(request.rules, "text")
    items = {asset.id: text_items(asset.content) for asset in request.assets}
    code = {asset_id: coded(found, guard, rules) for asset_id, found in items.items()}
    judged_rules = criteria(request.rules, request.custom_rules, "text")
    allowed = frozenset(item.id for item in judged_rules)
    model_findings: dict[str, list[Finding]] = {asset.id: [] for asset in request.assets}
    usages: list[Usage | None] = []
    if judged_rules:
        data = {
            **shared_data(request, judged_rules),
            "assets": [asset.model_dump(by_alias=True) for asset in request.assets],
        }
        try:
            answer, usages = await review(
                clients, model, copy_instructions(), data_block(data), DraftReview
            )
        except Exception:
            logger.exception("Brand Guardian couldn't review the copy.")
            return failed()
        for draft in answer.findings:
            if draft.asset_id not in items:
                continue
            finding = judged(
                draft.rule,
                draft.field,
                draft.index,
                draft.message,
                draft.evidence,
                allowed,
                fields_of(items[draft.asset_id]),
            )
            if finding is not None:
                model_findings[draft.asset_id].append(finding)
    results = [
        AssetFindings(asset_id=asset.id, findings=merged(code[asset.id], model_findings[asset.id]))
        for asset in request.assets
    ]
    return respond(results, model, COPY_PROMPT_VERSION, usages)


@router.post("/check/image", response_model=CheckResponse)
async def check_image(request: ImageCheckRequest, clients: ClientSource) -> Any:
    model = model_name()
    guard = guard_for(
        request.brief, request.products, request.brand_kit, request.language, request.brand
    )
    rules = enabled(request.rules, "image")
    without = frozenset() if request.references else frozenset({ACCURACY})
    judged_rules = criteria(request.rules, request.custom_rules, "image", without)
    allowed = frozenset(item.id for item in judged_rules)
    if not judged_rules:
        return respond(
            [AssetFindings(asset_id=request.asset_id, findings=[])], model, IMAGE_PROMPT_VERSION, []
        )
    data = {
        **shared_data(request, judged_rules),
        "format": request.format,
        "referenceProducts": references_data(request.references),
    }
    parts = [
        types.Part.from_text(text="The image to check:"),
        photo_part(request.image),
        *reference_parts(request.references),
        types.Part.from_text(text=data_block(data)),
    ]
    try:
        answer, usages = await review(
            clients,
            model,
            image_instructions(),
            [types.Content(role="user", parts=parts)],
            DraftImageReview,
        )
    except Exception:
        logger.exception("Brand Guardian couldn't review the image.")
        return failed()
    texts = [tidy(text)[:MAX_TEXT] for text in answer.on_image_text if tidy(text)]
    items = [
        Item("onImageText", index, text) for index, text in enumerate(texts[:MAX_ON_IMAGE_TEXT])
    ]
    fields: dict[str, int | None] = {"image": None, "onImageText": len(items)}
    found = [
        finding
        for draft in answer.findings
        if (
            finding := judged(
                draft.rule, draft.field, draft.index, draft.message, draft.evidence, allowed, fields
            )
        )
        is not None
    ]
    code = coded(items, guard, rules - {"language.match"})
    results = [AssetFindings(asset_id=request.asset_id, findings=merged(code, found))]
    return respond(results, model, IMAGE_PROMPT_VERSION, usages)


def frame_parts(frames: list[SampledFrame]) -> list[types.Part]:
    parts: list[types.Part] = []
    for index, frame in enumerate(frames):
        parts.append(types.Part.from_text(text=f"Frame {index} at {frame.at} s"))
        parts.append(types.Part.from_bytes(data=frame.data, mime_type="image/jpeg"))
    return parts


@router.post("/check/video", response_model=CheckResponse)
async def check_video(request: VideoCheckRequest, clients: ClientSource) -> Any:
    model = model_name()
    guard = guard_for(
        request.brief, request.products, request.brand_kit, request.language, request.brand
    )
    rules = enabled(request.rules, "video")
    items = [
        Item(field, index, text)
        for index, line in enumerate(request.script)
        for field, text in (("script", line.voiceover), ("captions", line.caption))
        if tidy(text)
    ]
    code = coded(items, guard, rules)
    without = frozenset() if request.references else frozenset({ACCURACY})
    judged_rules = criteria(request.rules, request.custom_rules, "video", without)
    allowed = frozenset(item.id for item in judged_rules)
    if not judged_rules:
        return respond(
            [AssetFindings(asset_id=request.asset_id, findings=code)],
            model,
            VIDEO_PROMPT_VERSION,
            [],
        )
    try:
        frames = await sample_frames(request.video.data)
    except FfmpegError:
        logger.exception("Brand Guardian couldn't read the video.")
        return JSONResponse(status_code=422, content={"error": "bad_video"})
    if not frames:
        return JSONResponse(status_code=422, content={"error": "bad_video"})
    data = {
        **shared_data(request, judged_rules),
        "seconds": request.seconds,
        "frames": [{"index": index, "at": frame.at} for index, frame in enumerate(frames)],
        "script": [line.model_dump(by_alias=True) for line in request.script],
        "referenceProducts": references_data(request.references),
    }
    parts = [
        types.Part.from_text(text="Frames from the video, in order:"),
        *frame_parts(frames),
        *reference_parts(request.references),
    ]
    if request.logo is not None:
        parts += [types.Part.from_text(text="The shop's logo:"), photo_part(request.logo)]
    parts.append(types.Part.from_text(text=data_block(data)))
    try:
        answer, usages = await review(
            clients,
            model,
            video_instructions(),
            [types.Content(role="user", parts=parts)],
            DraftVideoReview,
        )
    except Exception:
        logger.exception("Brand Guardian couldn't review the video.")
        return failed()
    lines = len(request.script)
    fields: dict[str, int | None] = {"script": lines, "captions": lines, "frames": len(frames)}
    found: list[Finding] = []
    for draft in answer.findings:
        finding = judged(
            draft.rule, draft.field, draft.index, draft.message, draft.evidence, allowed, fields
        )
        if finding is None:
            continue
        if finding.field == "frames" and finding.index is not None:
            finding = finding.model_copy(update={"at": frames[finding.index].at})
        found.append(finding)
    results = [AssetFindings(asset_id=request.asset_id, findings=merged(code, found))]
    return respond(results, model, VIDEO_PROMPT_VERSION, usages)
