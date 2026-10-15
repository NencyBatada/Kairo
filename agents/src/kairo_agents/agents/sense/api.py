import asyncio
import logging
import os
from collections.abc import Callable
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.genai import types

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.sense.models import (
    ExplainedTopic,
    ExplainRequest,
    ExplainResponse,
    ExplainTopic,
    MatchAnswer,
    MatchRequest,
    MatchResponse,
    MatchTopic,
    Reason,
    Source,
)
from kairo_agents.agents.sense.prompt import (
    EXPLAIN_PROMPT_VERSION,
    MATCH_PROMPT_VERSION,
    NO_SOURCE,
    data_block,
    explain_instructions,
    match_instructions,
)

DEFAULT_MODEL = "gemini-3.5-flash"
MAX_TOPICS = 30
MAX_KEYWORDS = 15
MAX_REASONS = 4
MAX_SOURCES = 5
MAX_TEXT = 200
EXPLAIN_AT_ONCE = 4

logger = logging.getLogger(__name__)
router = APIRouter()


def model_name() -> str:
    return os.environ.get("KAIRO_SENSE_MODEL") or DEFAULT_MODEL


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


@router.post("/sense/match", response_model=MatchResponse)
async def match(request: MatchRequest, clients: ClientSource) -> Any:
    model = model_name()
    try:
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data_block(request),
            config=types.GenerateContentConfig(
                system_instruction=match_instructions(),
                response_mime_type="application/json",
                response_schema=MatchAnswer,
                temperature=0.2,
            ),
        )
        answer = MatchAnswer.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Sense couldn't match the keywords to the catalogue.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return MatchResponse(
        topics=clean_topics(answer.topics, request),
        model=model,
        prompt_version=MATCH_PROMPT_VERSION,
    )


@router.post("/sense/explain", response_model=ExplainResponse)
async def explain(request: ExplainRequest, clients: ClientSource) -> Any:
    model = model_name()
    client = clients()
    limit = asyncio.Semaphore(EXPLAIN_AT_ONCE)

    async def one(topic: ExplainTopic) -> ExplainedTopic | None:
        async with limit:
            try:
                response = await client.aio.models.generate_content(
                    model=model,
                    contents=data_block(
                        {
                            "brand": request.brand,
                            "market": request.market.model_dump(by_alias=True),
                            "topic": topic.model_dump(by_alias=True, exclude={"id"}),
                        }
                    ),
                    config=types.GenerateContentConfig(
                        system_instruction=explain_instructions(),
                        tools=[types.Tool(google_search=types.GoogleSearch())],
                        temperature=0.2,
                    ),
                )
            except Exception:
                logger.exception("Sense couldn't explain the topic %s.", topic.id)
                return None
            return ExplainedTopic(id=topic.id, reasons=grounded_reasons(response))

    explained = await asyncio.gather(*(one(topic) for topic in request.topics))
    if all(topic is None for topic in explained):
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return ExplainResponse(
        topics=[topic for topic in explained if topic is not None],
        model=model,
        prompt_version=EXPLAIN_PROMPT_VERSION,
    )


def clean_topics(topics: list[MatchTopic], request: MatchRequest) -> list[MatchTopic]:
    keywords = {keyword.text.casefold(): keyword.text for keyword in request.keywords}
    categories = {category.name for category in request.categories}
    used: set[str] = set()
    cleaned: list[MatchTopic] = []
    for topic in topics:
        name = topic.name.strip()[:MAX_TEXT]
        chosen: list[str] = []
        for keyword in topic.keywords:
            text = keywords.get(keyword.strip().casefold())
            if text is not None and text.casefold() not in used and len(chosen) < MAX_KEYWORDS:
                chosen.append(text)
                used.add(text.casefold())
        if not name or not chosen:
            continue
        cleaned.append(
            MatchTopic(
                name=name,
                keywords=chosen,
                categories=list(dict.fromkeys(c for c in topic.categories if c in categories)),
                fit=min(max(topic.fit, 0.0), 1.0),
                fit_reason=topic.fit_reason.strip()[:MAX_TEXT],
            )
        )
        if len(cleaned) == MAX_TOPICS:
            break
    return cleaned


def grounded_reasons(response: Any) -> list[Reason]:
    text = response.text or ""
    if not text.strip() or text.strip() == NO_SOURCE:
        return []
    candidates = getattr(response, "candidates", None) or []
    metadata = getattr(candidates[0], "grounding_metadata", None) if candidates else None
    chunks = list(getattr(metadata, "grounding_chunks", None) or [])
    supports = list(getattr(metadata, "grounding_supports", None) or [])

    reasons: list[Reason] = []
    for line, start, end in reason_lines(text):
        sources: list[Source] = []
        for support in supports:
            if not supports_line(support, line, start, end):
                continue
            for index in getattr(support, "grounding_chunk_indices", None) or []:
                if 0 <= index < len(chunks):
                    source = web_source(chunks[index])
                    if source is not None and all(s.url != source.url for s in sources):
                        sources.append(source)
        if sources:
            reasons.append(Reason(text=line[:MAX_TEXT], sources=sources[:MAX_SOURCES]))
        if len(reasons) == MAX_REASONS:
            break
    return reasons


def reason_lines(text: str) -> list[tuple[str, int, int]]:
    lines: list[tuple[str, int, int]] = []
    offset = 0
    for raw in text.splitlines(keepends=True):
        size = len(raw.encode("utf-8"))
        line = raw.strip()
        if line.startswith(("- ", "* ", "• ")):
            line = line[2:].strip()
        if line and line != NO_SOURCE:
            lines.append((line, offset, offset + size))
        offset += size
    return lines


def supports_line(support: Any, line: str, start: int, end: int) -> bool:
    segment = getattr(support, "segment", None)
    if segment is None:
        return False
    piece = (getattr(segment, "text", None) or "").strip()
    if piece and (piece in line or line in piece):
        return True
    first = getattr(segment, "start_index", None) or 0
    last = getattr(segment, "end_index", None)
    return last is not None and first < end and last > start


def web_source(chunk: Any) -> Source | None:
    web = getattr(chunk, "web", None)
    url = getattr(web, "uri", None) if web is not None else None
    if not url or not url.startswith(("https://", "http://")):
        return None
    title = getattr(web, "title", None) or getattr(web, "domain", None) or url
    return Source(title=title[:300], url=url)
