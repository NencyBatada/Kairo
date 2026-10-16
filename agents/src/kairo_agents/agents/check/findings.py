from collections.abc import Iterable
from dataclasses import dataclass

from kairo_agents.agents.check.models import CheckRule, CustomRule, Finding, Medium
from kairo_agents.agents.create import languages
from kairo_agents.agents.create.copy import Guard, tidy
from kairo_agents.agents.create.models import AssetText

CORE_ONLY = frozenset({"format.text_limits", "format.media_spec", "policy.political"})
LANGUAGE_MATCH = "language.match"
ACCURACY = "accuracy.product"
MAX_FINDINGS = 30
MAX_MESSAGE = 300
MAX_EVIDENCE = 200
CUSTOM_NAME = "Custom brand rule"


@dataclass(frozen=True)
class Criterion:
    id: str
    name: str
    description: str


@dataclass(frozen=True)
class Item:
    field: str
    index: int | None
    text: str
    keyword: bool = False
    language: bool = True


def enabled(rules: Iterable[CheckRule], medium: Medium) -> frozenset[str]:
    return frozenset(rule.id for rule in rules if medium in rule.applies_to)


def criteria(
    rules: Iterable[CheckRule],
    custom_rules: Iterable[CustomRule],
    medium: Medium,
    without: frozenset[str] = frozenset(),
) -> list[Criterion]:
    chosen: dict[str, Criterion] = {}
    for rule in rules:
        if medium in rule.applies_to and rule.id not in CORE_ONLY and rule.id not in without:
            chosen.setdefault(rule.id, Criterion(rule.id, rule.name, rule.description))
    for custom in custom_rules:
        if custom.applies_to in (medium, "all"):
            chosen.setdefault(custom.id, Criterion(custom.id, CUSTOM_NAME, custom.text))
    return list(chosen.values())


def criteria_data(chosen: Iterable[Criterion]) -> list[dict[str, str]]:
    return [{"id": item.id, "name": item.name, "description": item.description} for item in chosen]


def text_items(content: AssetText) -> list[Item]:
    items = [
        Item("keywords", index, keyword.text, keyword=True)
        for index, keyword in enumerate(content.keywords)
    ]
    for field, values in (
        ("headlines", content.headlines),
        ("longHeadlines", content.long_headlines),
        ("descriptions", content.descriptions),
    ):
        items += [Item(field, index, value) for index, value in enumerate(values)]
    for field, value, language in (
        ("path1", content.path1, False),
        ("path2", content.path2, False),
        ("businessName", content.business_name, False),
        ("title", content.title, True),
    ):
        if value:
            items.append(Item(field, None, value, language=language))
    return [item for item in items if tidy(item.text)]


def fields_of(items: Iterable[Item]) -> dict[str, int | None]:
    fields: dict[str, int | None] = {}
    for item in items:
        if item.index is None:
            fields[item.field] = None
        else:
            fields[item.field] = max(fields.get(item.field) or 0, item.index + 1)
    return fields


def coded(items: Iterable[Item], guard: Guard, rules: frozenset[str]) -> list[Finding]:
    language = languages.language_for(guard.language)
    found: list[Finding] = []
    for item in items:
        for hit in guard.findings(item.text):
            if hit.rule in rules:
                found.append(
                    Finding(
                        rule=hit.rule,
                        field=item.field,
                        index=item.index,
                        message=hit.message,
                        evidence=hit.evidence,
                    )
                )
        if LANGUAGE_MATCH in rules and item.language and language is not None:
            fits = guard.fits_keyword(item.text) if item.keyword else guard.fits(item.text)
            if not fits:
                found.append(
                    Finding(
                        rule=LANGUAGE_MATCH,
                        field=item.field,
                        index=item.index,
                        message=f"Isn't written in {language.name}.",
                        evidence=tidy(item.text)[:MAX_EVIDENCE],
                    )
                )
    return found


def judged(
    rule: str,
    field: str,
    index: int,
    message: str,
    evidence: str,
    allowed: frozenset[str],
    fields: dict[str, int | None],
) -> Finding | None:
    text = tidy(message)[:MAX_MESSAGE]
    if rule not in allowed or not text:
        return None
    chosen: str | None = field if field in fields else None
    place: int | None = None
    if chosen is not None:
        count = fields[chosen]
        if count is not None and 0 <= index < count:
            place = index
    return Finding(
        rule=rule,
        field=chosen,
        index=place,
        message=text,
        evidence=tidy(evidence)[:MAX_EVIDENCE] or None,
    )


def merged(code: list[Finding], model: list[Finding]) -> list[Finding]:
    places = {(item.rule, item.field, item.index) for item in code}
    kept = list(code)
    seen = {(item.rule, item.field, item.index, item.message.casefold()) for item in code}
    for item in model:
        key = (item.rule, item.field, item.index, item.message.casefold())
        if key in seen:
            continue
        if item.field is not None and (item.rule, item.field, item.index) in places:
            continue
        seen.add(key)
        kept.append(item)
    return kept[:MAX_FINDINGS]
