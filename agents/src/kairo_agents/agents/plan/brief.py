import math
from datetime import date, timedelta
from typing import Any

from kairo_agents.agents.plan.models import (
    BriefInstructionRequest,
    DraftedSmartBrief,
    FieldReasons,
    ProposedBrief,
    SmartBrief,
    SmartBriefRequest,
)

CAMPAIGN_TYPES = ("search", "shopping", "demand_gen")
KPIS = ("orders", "conversions", "revenue")
MAX_TITLE = 120
MAX_OBJECTIVE = 300
MAX_AUDIENCE = 200
MAX_MESSAGE = 150
MAX_REASON = 500
MAX_SUMMARY = 300
MAX_AUDIENCES = 3
MAX_MESSAGES = 4
MAX_PRODUCTS = 6
MAX_LANGUAGES = 5
MAX_BUDGET = 1_000_000_000_000.0
FIELD_REASONS = {
    "objective": "objective",
    "kpi": "kpi",
    "campaign_types": "campaign_types",
    "budget": "budget",
    "start_date": "dates",
    "end_date": "dates",
    "regions": "regions",
    "audiences": "audiences",
    "key_messages": "key_messages",
    "product_ids": "products",
    "languages": "languages",
}


def clean_smart_brief(drafted: DraftedSmartBrief, request: SmartBriefRequest) -> SmartBrief | None:
    title = cut(drafted.title, MAX_TITLE)
    objective = cut(drafted.objective, MAX_OBJECTIVE)
    audiences = texts(drafted.audiences, MAX_AUDIENCE, MAX_AUDIENCES)
    key_messages = texts(drafted.key_messages, MAX_MESSAGE, MAX_MESSAGES)
    if not (title and objective and audiences and key_messages):
        return None
    regions = pick(drafted.regions, [region.name for region in request.opportunity.regions])
    products = [product.id for product in request.products]
    chosen_products = pick(drafted.product_ids, products)[:MAX_PRODUCTS]
    start, end = clean_dates(drafted.start_date, drafted.end_date, request)
    return SmartBrief(
        title=title,
        objective=objective,
        kpi=choice(drafted.kpi, KPIS) or "orders",
        campaign_types=pick(drafted.campaign_types, list(CAMPAIGN_TYPES)) or default_types(request),
        budget=clean_budget(drafted.budget, request),
        start_date=start.isoformat(),
        end_date=end.isoformat(),
        regions=regions or [region.name for region in request.opportunity.regions],
        audiences=audiences,
        key_messages=key_messages,
        product_ids=chosen_products or products[:3],
        languages=clean_languages(drafted.languages, request.languages),
        reasons=clean_reasons(drafted.reasons),
    )


def clean_proposal(proposed: ProposedBrief, request: BriefInstructionRequest) -> ProposedBrief:
    current = request.brief
    options = request.options
    today = parse_date(request.today) or date.today()
    changes: dict[str, Any] = {}
    title = cut(proposed.title, MAX_TITLE)
    if title and title != current.title:
        changes["title"] = title
    objective = cut(proposed.objective, MAX_OBJECTIVE)
    if objective and objective != current.objective:
        changes["objective"] = objective
    kpi = choice(proposed.kpi, KPIS)
    if kpi and kpi != current.kpi:
        changes["kpi"] = kpi
    if proposed.campaign_types is not None:
        types = pick(proposed.campaign_types, list(CAMPAIGN_TYPES))
        if types and types != current.campaign_types:
            changes["campaign_types"] = types
    budget = proposed.budget
    if budget is not None and math.isfinite(budget) and 0 < budget <= MAX_BUDGET:
        rounded = round(budget, 2)
        if rounded != current.budget:
            changes["budget"] = rounded
    start = parse_date(proposed.start_date)
    end = parse_date(proposed.end_date)
    new_start = start if start and start >= today else None
    new_end = end
    effective_start = new_start or parse_date(current.start_date)
    effective_end = new_end or parse_date(current.end_date)
    if not (effective_start and effective_end and effective_end < effective_start):
        if new_start and new_start.isoformat() != current.start_date:
            changes["start_date"] = new_start.isoformat()
        if new_end and new_end.isoformat() != current.end_date:
            changes["end_date"] = new_end.isoformat()
    if proposed.regions is not None:
        regions = pick(proposed.regions, list(options.regions))
        if regions and regions != current.regions:
            changes["regions"] = regions
    if proposed.audiences is not None:
        audiences = texts(proposed.audiences, MAX_AUDIENCE, MAX_AUDIENCES)
        if audiences and audiences != current.audiences:
            changes["audiences"] = audiences
    if proposed.key_messages is not None:
        messages = texts(proposed.key_messages, MAX_MESSAGE, MAX_MESSAGES)
        if messages and messages != current.key_messages:
            changes["key_messages"] = messages
    if proposed.product_ids is not None:
        products = pick(proposed.product_ids, [product.id for product in options.products])
        if products and products[:MAX_PRODUCTS] != current.product_ids:
            changes["product_ids"] = products[:MAX_PRODUCTS]
    if proposed.languages is not None:
        languages = pick(
            [language.casefold() for language in proposed.languages], list(options.languages)
        )[:MAX_LANGUAGES]
        if languages and languages != current.languages:
            changes["languages"] = languages
    reasons = clean_reasons(proposed.reasons)
    kept = {FIELD_REASONS[field] for field in changes if field in FIELD_REASONS}
    return ProposedBrief(
        **changes,
        reasons=FieldReasons(
            **{name: value for name, value in reasons.model_dump().items() if name in kept}
        ),
        summary=cut(proposed.summary, MAX_SUMMARY) or "",
    )


def clean_dates(start_text: str, end_text: str, request: SmartBriefRequest) -> tuple[date, date]:
    guide = request.dates
    today = parse_date(guide.today) or date.today()
    suggested_start = max(parse_date(guide.suggested_start) or today, today)
    suggested_end = parse_date(guide.suggested_end) or suggested_start
    latest = parse_date(guide.latest_end) if guide.latest_end else None
    start = parse_date(start_text) or suggested_start
    if start < today:
        start = suggested_start
    end = parse_date(end_text) or suggested_end
    if end < start:
        end = max(suggested_end, start)
    if latest is not None:
        if start > latest:
            start = min(today, latest)
        if end > latest:
            end = latest
        if end < start:
            end = start
    if (end - start).days > 89:
        end = start + timedelta(days=89)
    return start, end


def clean_budget(amount: float, request: SmartBriefRequest) -> float:
    guide = request.budget
    low, high = min(guide.low, guide.high), max(guide.low, guide.high)
    if not math.isfinite(amount) or amount <= 0:
        amount = guide.suggested
    return float(round(min(max(amount, low), high)))


def clean_languages(languages: list[str], allowed: list[str]) -> list[str]:
    chosen = pick([language.casefold() for language in languages], allowed)
    if "en" in allowed and "en" not in chosen:
        chosen = ["en", *chosen]
    return (chosen or allowed[:1])[:MAX_LANGUAGES]


def default_types(request: SmartBriefRequest) -> list[str]:
    priced = any(product.price for product in request.products)
    return ["search", "shopping"] if priced else ["search"]


def clean_reasons(reasons: FieldReasons) -> FieldReasons:
    return FieldReasons(
        **{name: cut(value, MAX_REASON) or "" for name, value in reasons.model_dump().items()}
    )


def pick(values: list[str], allowed: list[str]) -> list[str]:
    known = {value.casefold(): value for value in allowed}
    return unique(known.get((value or "").strip().casefold()) for value in values)


def choice(value: str | None, allowed: tuple[str, ...]) -> str | None:
    text = (value or "").strip().casefold()
    return text if text in allowed else None


def texts(values: list[str], length: int, limit: int) -> list[str]:
    return unique(cut(value, length) for value in values)[:limit]


def parse_date(text: str | None) -> date | None:
    try:
        return date.fromisoformat((text or "").strip())
    except ValueError:
        return None


def cut(text: str | None, length: int) -> str | None:
    trimmed = (text or "").strip()
    return trimmed[:length] or None


def unique(values: Any) -> list[str]:
    return list(dict.fromkeys(value for value in values if value))
