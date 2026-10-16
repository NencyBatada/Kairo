from typing import Any

from kairo_agents.agents.learn.models import DraftLesson, LearnAd, LearnRequest, Lesson

KINDS = ("message", "visual", "audience", "region")
OUTCOMES = ("worked", "didnt_work")
PROVEN = ("worked", "no_effect")
MAX_LESSONS = 8
MAX_TITLE = 120
MAX_LESSON = 500
MAX_NOTE = 300
MIN_CLICKS = 100
MIN_CONVERSIONS = 15
CLEAR_GAP = 1.25


def proven(request: LearnRequest) -> bool:
    return request.result.outcome in PROVEN


def enough(ad: LearnAd) -> bool:
    return ad.clicks >= MIN_CLICKS or ad.conversions >= MIN_CONVERSIONS


def rates(ads: list[LearnAd]) -> dict[str, float]:
    found: dict[str, float] = {}
    groups: dict[str, list[LearnAd]] = {}
    for ad in ads:
        if enough(ad):
            groups.setdefault(ad.campaign_type, []).append(ad)
    for group in groups.values():
        by_conversions = sum(ad.conversions for ad in group) >= MIN_CONVERSIONS
        for ad in group:
            if by_conversions and ad.clicks > 0:
                found[ad.id] = ad.conversions / ad.clicks
            elif not by_conversions and ad.impressions > 0:
                found[ad.id] = ad.clicks / ad.impressions
    return found


def clear(ad: LearnAd, outcome: str, ads: list[LearnAd], rate: dict[str, float]) -> bool:
    if ad.id not in rate:
        return False
    peers = [
        rate[other.id]
        for other in ads
        if other.campaign_type == ad.campaign_type and other.id in rate
    ]
    if len(peers) < 2:
        return False
    best, worst = max(peers), min(peers)
    if best <= 0 or best < worst * CLEAR_GAP:
        return False
    if outcome == "worked":
        return rate[ad.id] > 0 and rate[ad.id] >= worst * CLEAR_GAP
    return rate[ad.id] * CLEAR_GAP <= best


def cut(text: str | None, length: int) -> str | None:
    value = " ".join((text or "").split())
    return value[:length].rstrip() or None


def clean_lessons(drafted: list[DraftLesson], request: LearnRequest) -> list[Lesson]:
    if not proven(request):
        return []
    expected = "worked" if request.result.outcome == "worked" else "didnt_work"
    ads = {ad.id: ad for ad in request.ads}
    rate = rates(request.ads)
    regions = {region.name.casefold(): region for region in request.regions}
    lessons: list[Lesson] = []
    for draft in drafted:
        kind = draft.kind.strip().casefold()
        outcome = draft.outcome.strip().casefold()
        title = cut(draft.title, MAX_TITLE)
        text = cut(draft.lesson, MAX_LESSON)
        if kind not in KINDS or outcome not in OUTCOMES or not title or not text:
            continue
        if any(lesson.title.casefold() == title.casefold() for lesson in lessons):
            continue
        cited: list[LearnAd] = []
        named: list[str] = []
        if kind in ("message", "visual"):
            wanted = list(dict.fromkeys(draft.ad_ids))
            cited = [ads[ad_id] for ad_id in wanted if ad_id in ads]
            if not cited or len(cited) != len(wanted):
                continue
            if kind == "visual" and not all(ad.visual for ad in cited):
                continue
            if not all(clear(ad, outcome, request.ads, rate) for ad in cited):
                continue
        else:
            if outcome != expected:
                continue
            named = [
                regions[name.casefold()].name
                for name in dict.fromkeys(draft.regions)
                if name.casefold() in regions
            ]
            if kind == "region" and not named:
                continue
        lessons.append(
            Lesson(
                kind=kind,
                outcome=outcome,
                title=title,
                lesson=text,
                ad_ids=[ad.id for ad in cited],
                regions=named,
                languages=languages(cited, request),
                evidence=evidence(draft, cited, named, request, rate),
            )
        )
        if len(lessons) == MAX_LESSONS:
            break
    return lessons


def languages(cited: list[LearnAd], request: LearnRequest) -> list[str]:
    found = [ad.language for ad in cited if ad.language]
    return list(dict.fromkeys(found or request.campaign.languages))


def evidence(
    draft: DraftLesson,
    cited: list[LearnAd],
    named: list[str],
    request: LearnRequest,
    rate: dict[str, float],
) -> dict[str, Any]:
    result = request.result
    found: dict[str, Any] = {
        "outcome": result.outcome,
        "lift": result.lift,
        "low": result.low,
        "high": result.high,
        "extra": result.extra,
        "costPerExtra": result.cost_per_extra,
        "currency": request.currency,
    }
    if cited:
        found["ads"] = [
            {
                "id": ad.id,
                "campaignType": ad.campaign_type,
                "impressions": ad.impressions,
                "clicks": ad.clicks,
                "conversions": ad.conversions,
                "rate": round(rate[ad.id], 4),
            }
            for ad in cited
        ]
    if named:
        wanted = {name.casefold() for name in named}
        found["regions"] = [
            {"name": region.name, "actual": region.actual, "expected": region.expected}
            for region in request.regions
            if region.name.casefold() in wanted
        ]
    note = cut(draft.evidence, MAX_NOTE)
    if note:
        found["note"] = note
    return found
