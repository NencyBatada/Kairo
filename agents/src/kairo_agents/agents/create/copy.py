import re
import unicodedata
from collections.abc import Iterable
from dataclasses import dataclass, field
from typing import Any

from kairo_agents.agents.create import languages, limits
from kairo_agents.agents.create.models import (
    AdGroupCopy,
    AssetText,
    CopyBrief,
    CopyProduct,
    DemandGenCopy,
    DraftAdGroup,
    DraftDemandGen,
    DraftKeyword,
    DraftRevision,
    DraftSearch,
    DraftTitles,
    Keyword,
    ShoppingTitle,
)

AMOUNT = re.compile(
    r"(?:₹|\brs\.?|\binr)\s*(\d[\d,]*(?:\.\d+)?)|(\d[\d,]*(?:\.\d+)?)\s*(?:₹|\brs\b|\binr\b|/-)",
    re.IGNORECASE,
)
PERCENT = re.compile(r"(\d+(?:\.\d+)?)\s*%")
CLAIMS = (
    "free shipping",
    "free delivery",
    "discount",
    "cashback",
    "cash back",
    "lowest price",
    "cheapest",
    "best price",
    "guarantee",
    "same day delivery",
    "same-day delivery",
    "next day delivery",
    "next-day delivery",
    "sale",
    "coupon",
)
TRANSLATED_CLAIMS = {
    "मुफ्त डिलीवरी": "free delivery",
    "मुफ़्त डिलीवरी": "free delivery",
    "फ्री डिलीवरी": "free delivery",
    "मुफ्त शिपिंग": "free shipping",
    "मुफ़्त शिपिंग": "free shipping",
    "छूट": "discount",
    "डिस्काउंट": "discount",
    "कैशबैक": "cashback",
    "सबसे कम कीमत": "lowest price",
    "सबसे सस्ता": "cheapest",
    "सबसे सस्ते": "cheapest",
    "गारंटी": "guarantee",
    "सेल": "sale",
    "कूपन": "coupon",
}
SYMBOLS = re.compile(r"[!@%,*=;:?\^`<>{}\[\]|~\"#$()/]")
GAPS = re.compile(r"[\s/]+")


@dataclass
class Guard:
    banned: list[str] = field(default_factory=list)
    competitors: list[str] = field(default_factory=list)
    facts: str = ""
    numbers: set[str] = field(default_factory=set)
    language: str = "en"
    names: frozenset[str] = frozenset()

    def fits(self, text: str) -> bool:
        return languages.fits(text, self.language, self.names)

    def fits_keyword(self, text: str) -> bool:
        return languages.fits_keyword(text, self.language, self.names)

    def allows(self, text: str) -> bool:
        lowered = text.casefold()
        for word in self.banned + self.competitors:
            if word and re.search(rf"(?<!\w){re.escape(word.casefold())}(?!\w)", lowered):
                return False
        for match in AMOUNT.finditer(text):
            number = normalized(match.group(1) or match.group(2))
            if number not in self.numbers:
                return False
        for match in PERCENT.finditer(text):
            if f"{match.group(1)}%" not in self.facts.replace(" %", "%"):
                return False
        for claim in CLAIMS:
            if re.search(rf"(?<!\w){re.escape(claim)}(?!\w)", lowered) and claim not in self.facts:
                return False
        for claim, meaning in TRANSLATED_CLAIMS.items():
            if claim in text and claim not in self.facts and meaning not in self.facts:
                return False
        return True


def guard_for(
    brief: CopyBrief,
    products: list[CopyProduct],
    brand_kit: dict[str, Any] | None,
    language: str = "en",
    brand: str = "",
) -> Guard:
    rules = (brand_kit or {}).get("rules") or {}
    banned = [str(word).strip() for word in rules.get("bannedWords") or [] if str(word).strip()]
    competitors = [
        str(entry.get("name", "")).strip()
        for entry in rules.get("competitors") or []
        if isinstance(entry, dict) and str(entry.get("policy", "")).casefold() == "never"
    ]
    facts = [brief.offer or "", *brief.key_messages]
    numbers: set[str] = set()
    for product in products:
        facts.extend([product.title, product.description or ""])
        for price in (product.price, product.sale_price):
            if price is not None:
                numbers.add(normalized(f"{price:.2f}"))
        if product.sale_price is not None and product.price and product.sale_price < product.price:
            facts.append("sale")
    for text in facts:
        for match in AMOUNT.finditer(text):
            numbers.add(normalized(match.group(1) or match.group(2)))
    names = {word.casefold() for word in brand.split()}
    for product in products:
        names.update(word.casefold() for word in (product.brand or "").split())
    return Guard(
        banned=banned,
        competitors=[name for name in competitors if name],
        facts=" ".join(facts).casefold(),
        numbers=numbers,
        language=language,
        names=frozenset(names),
    )


def normalized(number: str) -> str:
    plain = number.replace(",", "")
    if "." in plain:
        plain = plain.rstrip("0").rstrip(".")
    return plain


def tidy(text: str | None) -> str:
    return " ".join((text or "").split())


def emoji(text: str) -> bool:
    return any(unicodedata.category(character) == "So" for character in text)


def line(
    text: str | None, maximum: int, guard: Guard, headline: bool = False, script: bool = True
) -> str | None:
    value = tidy(text)
    if not value or limits.length(value) > maximum or emoji(value):
        return None
    if headline and "!" in value:
        return None
    if re.search(r"([!?.,।])\1", value):
        return None
    if script and not guard.fits(value):
        return None
    return value if guard.allows(value) else None


def lines(
    texts: Iterable[str | None],
    maximum: int,
    most: int,
    guard: Guard,
    headline: bool = False,
) -> list[str]:
    kept: dict[str, str] = {}
    for text in texts:
        value = line(text, maximum, guard, headline)
        if value is not None:
            kept.setdefault(value.casefold(), value)
    return list(kept.values())[:most]


def keywords(drafts: Iterable[DraftKeyword | Keyword], guard: Guard) -> list[Keyword]:
    kept: dict[str, Keyword] = {}
    for draft in drafts:
        text = tidy(draft.text).casefold()
        if (
            not text
            or limits.length(text) > limits.KEYWORD
            or len(text.split()) > limits.KEYWORD_WORDS
            or SYMBOLS.search(text)
            or emoji(text)
            or not guard.fits_keyword(text)
            or not guard.allows(text)
        ):
            continue
        match = draft.match.strip().casefold() if draft.match else ""
        kept.setdefault(
            text, Keyword(text=text, match=match if match in limits.MATCH_TYPES else "phrase")
        )
    return list(kept.values())[: limits.KEYWORDS[1]]


def path(text: str | None) -> str | None:
    value = SYMBOLS.sub("", GAPS.sub("-", tidy(text))).strip("-")
    if not value:
        return None
    return value if limits.length(value) <= limits.SEARCH_PATH else None


def final_url(product_ids: list[str], products: list[CopyProduct], brief: CopyBrief) -> str | None:
    links = {product.id: product.link for product in products if product.link}
    for product_id in product_ids:
        if product_id in links:
            return links[product_id]
    return brief.landing_pages[0] if brief.landing_pages else next(iter(links.values()), None)


@dataclass
class Cleaned:
    value: Any
    problems: list[str]


def clean_search(
    draft: DraftSearch, brief: CopyBrief, products: list[CopyProduct], guard: Guard
) -> Cleaned:
    known = [product.id for product in products]
    groups: list[AdGroupCopy] = []
    problems: list[str] = []
    names: set[str] = set()
    for drafted in draft.ad_groups:
        group = clean_ad_group(drafted, known, brief, products, guard)
        if group.value is None:
            problems.extend(group.problems)
            continue
        if group.value.name.casefold() in names:
            continue
        names.add(group.value.name.casefold())
        groups.append(group.value)
        if len(groups) == limits.AD_GROUPS[1]:
            break
    if len(groups) < limits.AD_GROUPS[0]:
        problems.append("Write at least one ad group that follows every rule.")
        return Cleaned(None, problems)
    return Cleaned(groups, [])


def clean_ad_group(
    drafted: DraftAdGroup,
    known: list[str],
    brief: CopyBrief,
    products: list[CopyProduct],
    guard: Guard,
) -> Cleaned:
    name = tidy(drafted.name)[: limits.AD_GROUP_NAME].strip() or "Ad group"
    product_ids = [key for key in dict.fromkeys(drafted.product_ids) if key in known]
    chosen = keywords(drafted.keywords, guard)
    headlines = lines(
        drafted.headlines, limits.SEARCH_HEADLINE, limits.SEARCH_HEADLINES[1], guard, True
    )
    descriptions = lines(
        drafted.descriptions, limits.SEARCH_DESCRIPTION, limits.SEARCH_DESCRIPTIONS[1], guard
    )
    problems = []
    if len(chosen) < limits.KEYWORDS[0]:
        problems.append(f"Ad group '{name}' needs at least one keyword.")
    if len(headlines) < limits.SEARCH_HEADLINES[0]:
        problems.append(
            f"Ad group '{name}' needs at least {limits.SEARCH_HEADLINES[0]} headlines of up to "
            f"{limits.SEARCH_HEADLINE} characters with no unsupported claims."
        )
    if len(descriptions) < limits.SEARCH_DESCRIPTIONS[0]:
        problems.append(
            f"Ad group '{name}' needs at least {limits.SEARCH_DESCRIPTIONS[0]} descriptions of up "
            f"to {limits.SEARCH_DESCRIPTION} characters with no unsupported claims."
        )
    if problems:
        return Cleaned(None, problems)
    return Cleaned(
        AdGroupCopy(
            name=name,
            product_ids=product_ids,
            keywords=chosen,
            headlines=headlines,
            descriptions=descriptions,
            path1=path(drafted.path1),
            path2=path(drafted.path2) if path(drafted.path1) else None,
            final_url=final_url(product_ids, products, brief),
        ),
        [],
    )


def business_name(brand: str) -> str:
    name = tidy(brand)
    if limits.length(name) <= limits.BUSINESS_NAME:
        return name
    words: list[str] = []
    for word in name.split():
        if limits.length(" ".join([*words, word])) > limits.BUSINESS_NAME:
            break
        words.append(word)
    return " ".join(words) or name[: limits.BUSINESS_NAME]


def call_to_action(value: str | None) -> str:
    chosen = tidy(value).casefold().replace(" ", "_").replace("-", "_")
    return chosen if chosen in limits.CALLS_TO_ACTION else limits.DEFAULT_CALL_TO_ACTION


def clean_demand_gen(
    draft: DraftDemandGen,
    brand: str,
    brief: CopyBrief,
    products: list[CopyProduct],
    guard: Guard,
) -> Cleaned:
    headlines = lines(
        draft.headlines, limits.DEMAND_GEN_HEADLINE, limits.DEMAND_GEN_HEADLINES[1], guard, True
    )
    long_headlines = lines(
        draft.long_headlines,
        limits.DEMAND_GEN_LONG_HEADLINE,
        limits.DEMAND_GEN_LONG_HEADLINES[1],
        guard,
    )
    descriptions = lines(
        draft.descriptions, limits.DEMAND_GEN_DESCRIPTION, limits.DEMAND_GEN_DESCRIPTIONS[1], guard
    )
    problems = []
    if not any(limits.length(text) <= limits.DEMAND_GEN_SHORT_HEADLINE for text in headlines):
        problems.append(
            f"Demand Gen needs headlines of up to {limits.DEMAND_GEN_HEADLINE} characters, with "
            f"at least one of up to {limits.DEMAND_GEN_SHORT_HEADLINE}."
        )
    if not long_headlines:
        problems.append(
            f"Demand Gen needs a long headline of up to {limits.DEMAND_GEN_LONG_HEADLINE} "
            "characters."
        )
    if not descriptions:
        problems.append(
            f"Demand Gen needs a description of up to {limits.DEMAND_GEN_DESCRIPTION} characters."
        )
    if problems:
        return Cleaned(None, problems)
    return Cleaned(
        DemandGenCopy(
            headlines=headlines,
            long_headlines=long_headlines,
            descriptions=descriptions,
            business_name=business_name(brand),
            call_to_action=call_to_action(draft.call_to_action),
            final_url=final_url([], products, brief),
        ),
        [],
    )


def clean_titles(draft: DraftTitles, products: list[CopyProduct], guard: Guard) -> Cleaned:
    known = {product.id for product in products}
    titles: dict[str, ShoppingTitle] = {}
    for drafted in draft.titles:
        product_id = drafted.product_id.strip()
        title = line(drafted.title, limits.SHOPPING_TITLE, guard, script=False)
        if product_id in known and title and product_id not in titles:
            titles[product_id] = ShoppingTitle(product_id=product_id, title=title)
    ordered = [titles[product.id] for product in products if product.id in titles]
    if not ordered:
        return Cleaned(
            None,
            [
                f"Write one shopping title of up to {limits.SHOPPING_TITLE} characters for each "
                "product, using its id."
            ],
        )
    return Cleaned(ordered, [])


def clean_revision(
    kind: str,
    draft: DraftRevision,
    current: AssetText,
    brand: str,
    brief: CopyBrief,
    products: list[CopyProduct],
    guard: Guard,
) -> Cleaned:
    if kind == "keyword_set":
        chosen = keywords(draft.keywords, guard)
        if not chosen:
            return Cleaned(None, ["Keep at least one keyword that follows the rules."])
        return Cleaned(current.model_copy(update={"keywords": chosen}), [])
    if kind == "search_ad":
        headlines = lines(
            draft.headlines, limits.SEARCH_HEADLINE, limits.SEARCH_HEADLINES[1], guard, True
        )
        descriptions = lines(
            draft.descriptions, limits.SEARCH_DESCRIPTION, limits.SEARCH_DESCRIPTIONS[1], guard
        )
        if (
            len(headlines) < limits.SEARCH_HEADLINES[0]
            or len(descriptions) < limits.SEARCH_DESCRIPTIONS[0]
        ):
            return Cleaned(
                None,
                [
                    f"Keep at least {limits.SEARCH_HEADLINES[0]} headlines of up to "
                    f"{limits.SEARCH_HEADLINE} characters and {limits.SEARCH_DESCRIPTIONS[0]} "
                    f"descriptions of up to {limits.SEARCH_DESCRIPTION} characters."
                ],
            )
        first = path(draft.path1)
        return Cleaned(
            current.model_copy(
                update={
                    "headlines": headlines,
                    "descriptions": descriptions,
                    "path1": first,
                    "path2": path(draft.path2) if first else None,
                }
            ),
            [],
        )
    if kind == "demand_gen_text":
        cleaned = clean_demand_gen(
            DraftDemandGen(
                headlines=draft.headlines,
                long_headlines=draft.long_headlines,
                descriptions=draft.descriptions,
                call_to_action=draft.call_to_action,
            ),
            brand,
            brief,
            products,
            guard,
        )
        if cleaned.value is None:
            return cleaned
        value: DemandGenCopy = cleaned.value
        return Cleaned(
            current.model_copy(
                update={
                    "headlines": value.headlines,
                    "long_headlines": value.long_headlines,
                    "descriptions": value.descriptions,
                    "business_name": value.business_name,
                    "call_to_action": value.call_to_action,
                }
            ),
            [],
        )
    title = line(draft.title, limits.SHOPPING_TITLE, guard, script=False)
    if title is None:
        return Cleaned(None, [f"Write a title of up to {limits.SHOPPING_TITLE} characters."])
    return Cleaned(current.model_copy(update={"title": title}), [])
