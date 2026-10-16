import re
from collections.abc import Iterable
from typing import Any

from kairo_agents.agents.prove.report_models import DraftedReport, ReportRequest

MAX_SUMMARY = 1200
MAX_REASONING = 1000
MAX_EXPLANATION = 800
RANGE_LEVEL = 90
RATIO_FIELDS = ("lift", "low", "high", "revenue_lift")
NUMBER = re.compile(r"\d[\d,]*(?:\.\d+)?")


def clean_report(drafted: DraftedReport, request: ReportRequest) -> DraftedReport | None:
    summary = cut(drafted.summary, MAX_SUMMARY)
    reasoning = cut(drafted.next_step_reasoning, MAX_REASONING)
    if not (summary and reasoning):
        return None
    inconclusive = request.outcome == "inconclusive"
    reason = cut(drafted.inconclusive_reason, MAX_EXPLANATION) if inconclusive else None
    advice = cut(drafted.next_test_advice, MAX_EXPLANATION) if inconclusive else None
    if inconclusive and not (reason and advice):
        return None
    report = DraftedReport(
        summary=summary,
        next_step_reasoning=reasoning,
        inconclusive_reason=reason,
        next_test_advice=advice,
    )
    allowed = allowed_numbers(request)
    texts = [summary, reasoning, reason or "", advice or ""]
    if any(not supported(token, allowed) for text in texts for token in NUMBER.findall(text)):
        return None
    return report


def allowed_numbers(request: ReportRequest) -> set[float]:
    values: set[float] = {float(RANGE_LEVEL)}
    content = request.model_dump()
    for value in walk(content):
        if isinstance(value, bool):
            continue
        if isinstance(value, int | float):
            values.add(abs(float(value)))
        elif isinstance(value, str):
            values.update(parse(token) for token in NUMBER.findall(value))
    ratios = [getattr(request, field) for field in RATIO_FIELDS]
    ratios += [
        value for region in request.regions for value in (region.lift, region.low, region.high)
    ]
    values.update(abs(ratio) * 100 for ratio in ratios if ratio is not None)
    days = [request.days, request.planned_days]
    if request.next_test is not None and request.next_test.days is not None:
        days.append(request.next_test.days)
    values.update(day / 7 for day in days if day % 7 == 0)
    return values


def supported(token: str, allowed: set[float]) -> bool:
    number = parse(token)
    decimals = len(token.split(".")[1]) if "." in token else 0
    return any(abs(round(value, decimals) - number) < 1e-9 for value in allowed)


def walk(value: Any) -> Iterable[Any]:
    if isinstance(value, dict):
        for item in value.values():
            yield from walk(item)
    elif isinstance(value, list):
        for item in value:
            yield from walk(item)
    else:
        yield value


def parse(token: str) -> float:
    return float(token.replace(",", ""))


def cut(text: str | None, length: int) -> str | None:
    trimmed = (text or "").strip()
    return trimmed[:length] or None
