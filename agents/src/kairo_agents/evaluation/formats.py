import asyncio
import math
import re
import subprocess
import tempfile
from datetime import date
from io import BytesIO
from pathlib import Path

from PIL import Image

from kairo_agents.agents.create import limits
from kairo_agents.agents.create.copy import guard_for
from kairo_agents.agents.create.models import CopyRequest, CopyResponse
from kairo_agents.agents.plan.models import SmartBriefRequest, SmartBriefResponse
from kairo_agents.media.ffmpeg import ffmpeg_path
from kairo_agents.media.formats import FORMATS


def brief_errors(answer: SmartBriefResponse, request: SmartBriefRequest) -> list[str]:
    brief = answer.brief
    errors = []
    if (
        not math.isfinite(brief.budget)
        or not request.budget.low <= brief.budget <= request.budget.high
    ):
        errors.append("budget_outside_fixture_bounds")
    if not brief.product_ids or not set(brief.product_ids) <= {p.id for p in request.products}:
        errors.append("unsupported_products")
    if not brief.regions or not set(brief.regions) <= {r.name for r in request.opportunity.regions}:
        errors.append("unsupported_regions")
    if not brief.languages or not set(brief.languages) <= set(request.languages):
        errors.append("unsupported_languages")
    try:
        if (
            not date.fromisoformat(request.dates.today)
            <= date.fromisoformat(brief.start_date)
            <= date.fromisoformat(brief.end_date)
        ):
            errors.append("invalid_dates")
    except ValueError:
        errors.append("invalid_dates")
    if not all((brief.title.strip(), brief.objective.strip(), brief.audiences, brief.key_messages)):
        errors.append("missing_brief_fields")
    if not all(value.strip() for value in brief.reasons.model_dump().values()):
        errors.append("missing_field_reasons")
    return errors


def copy_errors(answer: CopyResponse, request: CopyRequest) -> list[str]:
    errors = []
    assets = answer.assets
    guard = guard_for(
        request.brief, request.products, request.brand_kit, request.language, request.brand
    )

    def lines(values: list[str], minimum: int, maximum: int, length: int) -> None:
        if not minimum <= len(values) <= maximum:
            errors.append("invalid_text_count")
        for text in values:
            if not text.strip() or limits.length(text) > length:
                errors.append("invalid_text_length")
            if guard.findings(text) or not guard.fits(text):
                errors.append("invalid_text_content")

    if not limits.AD_GROUPS[0] <= len(assets.ad_groups) <= limits.AD_GROUPS[1]:
        errors.append("invalid_ad_group_count")
    for group in assets.ad_groups:
        lines(group.headlines, *limits.SEARCH_HEADLINES, limits.SEARCH_HEADLINE)
        lines(group.descriptions, *limits.SEARCH_DESCRIPTIONS, limits.SEARCH_DESCRIPTION)
        if not group.product_ids or not set(group.product_ids) <= {p.id for p in request.products}:
            errors.append("unsupported_products")
        if not limits.KEYWORDS[0] <= len(group.keywords) <= limits.KEYWORDS[1]:
            errors.append("invalid_keyword_count")
        for keyword in group.keywords:
            if (
                limits.length(keyword.text) > limits.KEYWORD
                or len(keyword.text.split()) > limits.KEYWORD_WORDS
            ):
                errors.append("invalid_keyword_length")
            if keyword.match not in limits.MATCH_TYPES or not guard.fits_keyword(keyword.text):
                errors.append("invalid_keyword")
        if any(
            limits.length(value or "") > limits.SEARCH_PATH for value in (group.path1, group.path2)
        ):
            errors.append("invalid_search_path")
    demand = assets.demand_gen
    if demand is None:
        errors.append("missing_demand_gen")
    else:
        lines(demand.headlines, *limits.DEMAND_GEN_HEADLINES, limits.DEMAND_GEN_HEADLINE)
        lines(
            demand.long_headlines,
            *limits.DEMAND_GEN_LONG_HEADLINES,
            limits.DEMAND_GEN_LONG_HEADLINE,
        )
        lines(demand.descriptions, *limits.DEMAND_GEN_DESCRIPTIONS, limits.DEMAND_GEN_DESCRIPTION)
        if not any(
            limits.length(text) <= limits.DEMAND_GEN_SHORT_HEADLINE for text in demand.headlines
        ):
            errors.append("missing_short_headline")
        if limits.length(demand.business_name) > limits.BUSINESS_NAME:
            errors.append("business_name_too_long")
        if demand.call_to_action not in limits.CALLS_TO_ACTION:
            errors.append("invalid_call_to_action")
    if {title.product_id for title in assets.shopping_titles} != {p.id for p in request.products}:
        errors.append("missing_shopping_titles")
    for title in assets.shopping_titles:
        lines([title.title], 1, 1, limits.SHOPPING_TITLE)
    return errors


def image_errors(data: bytes, format_name: str) -> list[str]:
    target = FORMATS[format_name]
    with Image.open(BytesIO(data)) as image:
        if image.format != "JPEG" or image.size != (target.width, target.height):
            return ["invalid_generated_image"]
        image.verify()
    return []


async def video_errors(data: bytes, seconds: float) -> list[str]:
    def inspect() -> list[str]:
        with tempfile.TemporaryDirectory(prefix="kairo-eval-video-") as folder:
            path = Path(folder) / "clip.mp4"
            path.write_bytes(data)
            run = subprocess.run(
                [ffmpeg_path(), "-hide_banner", "-i", str(path), "-f", "null", "-"],
                capture_output=True,
                text=True,
                timeout=30,
            )
            duration = re.search(r"Duration:\s*(\d+):(\d+):([\d.]+)", run.stderr)
            dimensions = re.search(r"Video:.*?(\d{3,5})x(\d{3,5})", run.stderr)
            if run.returncode or duration is None or dimensions is None:
                return ["invalid_generated_video"]
            hours, minutes, fraction = map(float, duration.groups())
            actual = hours * 3600 + minutes * 60 + fraction
            if abs(actual - seconds) > 0.25 or dimensions.groups() != ("1080", "1920"):
                return ["invalid_video_duration_or_dimensions"]
            return []

    return await asyncio.to_thread(inspect)
