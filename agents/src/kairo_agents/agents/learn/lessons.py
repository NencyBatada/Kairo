import math
from typing import Any

from kairo_agents.agents.learn.models import (
    DraftLesson,
    LearnAd,
    LearnRegion,
    LearnRequest,
    Lesson,
)

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
CLEAR_Z = 1.96


def proven(request: LearnRequest) -> bool:
    return request.result.outcome in PROVEN


def enough(ad: LearnAd) -> bool:
    return ad.clicks >= MIN_CLICKS or ad.conversions >= MIN_CONVERSIONS


def groups(ads: list[LearnAd]) -> list[tuple[bool, list[LearnAd]]]:
    found: dict[str, list[LearnAd]] = {}
    for ad in ads:
        if enough(ad):
            found.setdefault(ad.campaign_type, []).append(ad)
    return [
        (sum(ad.conversions for ad in group) >= MIN_CONVERSIONS, group) for group in found.values()
    ]


def counts(ad: LearnAd, by_conversions: bool) -> tuple[float, float]:
    return (ad.conversions, ad.clicks) if by_conversions else (ad.clicks, ad.impressions)


def rates(ads: list[LearnAd]) -> dict[str, float]:
    found: dict[str, float] = {}
    for by_conversions, group in groups(ads):
        for ad in group:
            hits, trials = counts(ad, by_conversions)
            if trials > 0:
                found[ad.id] = hits / trials
    return found


def verdicts(ads: list[LearnAd]) -> dict[str, str]:
    found: dict[str, str] = {}
    for by_conversions, group in groups(ads):
        if len(group) < 2:
            continue
        for ad in group:
            hits, trials = counts(ad, by_conversions)
            rest = [counts(other, by_conversions) for other in group if other is not ad]
            rest_hits, rest_trials = sum(hit for hit, _ in rest), sum(trial for _, trial in rest)
            if trials <= 0 or rest_trials <= 0:
                continue
            pooled = (hits + rest_hits) / (trials + rest_trials)
            if pooled <= 0 or pooled >= 1:
                continue
            spread = math.sqrt(pooled * (1 - pooled) * (1 / trials + 1 / rest_trials))
            mine, theirs = hits / trials, rest_hits / rest_trials
            z = (mine - theirs) / spread
            if z >= CLEAR_Z and mine >= theirs * CLEAR_GAP:
                found[ad.id] = "worked"
            elif z <= -CLEAR_Z and mine * CLEAR_GAP <= theirs:
                found[ad.id] = "didnt_work"
    return found


def cut(text: str | None, length: int) -> str | None:
    value = " ".join((text or "").split())
    return value[:length].rstrip() or None


def clean_lessons(drafted: list[DraftLesson], request: LearnRequest) -> list[Lesson]:
    if not proven(request):
        return []
    expected = "worked" if request.result.outcome == "worked" else "didnt_work"
    ads = {ad.id: ad for ad in request.ads}
    rate = rates(request.ads)
    verdict = verdicts(request.ads)
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
            if not all(verdict.get(ad.id) == outcome for ad in cited):
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
            if kind == "region" and not all(
                region_fits(regions[name.casefold()], outcome) for name in named
            ):
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


def region_fits(region: LearnRegion, outcome: str) -> bool:
    if region.lift is None:
        return False
    proven_gain = region.conclusive and region.lift > 0
    return proven_gain if outcome == "worked" else not proven_gain


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
            {
                "name": region.name,
                "conclusive": region.conclusive,
                "lift": region.lift,
                "low": region.low,
                "high": region.high,
                "extra": region.extra,
            }
            for region in request.regions
            if region.name.casefold() in wanted
        ]
    note = cut(draft.evidence, MAX_NOTE)
    if note:
        found["note"] = note
    return found
