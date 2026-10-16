from typing import Annotated, Any, Literal

from pydantic import Field

from kairo_agents.agents.brand_kit.models import CamelModel
from kairo_agents.agents.plan.models import CampaignType, LanguageCode, Usage

ProductKey = Annotated[str, Field(pattern=r"^p[0-9]{1,2}$")]
Line = Annotated[str, Field(max_length=200)]
Url = Annotated[str, Field(max_length=2000)]
TextKind = Literal["keyword_set", "search_ad", "demand_gen_text", "shopping_title"]


class CopyProduct(CamelModel):
    id: ProductKey
    title: str = Field(min_length=1, max_length=300)
    description: str | None = Field(default=None, max_length=2000)
    brand: str | None = Field(default=None, max_length=100)
    category: str | None = Field(default=None, max_length=750)
    price: float | None = Field(default=None, ge=0)
    sale_price: float | None = Field(default=None, ge=0)
    currency: str | None = Field(default=None, max_length=3)
    link: Url | None = None
    gtin: str | None = Field(default=None, max_length=50)


class CopyBrief(CamelModel):
    title: str = Field(min_length=1, max_length=200)
    objective: str = Field(default="", max_length=500)
    audiences: list[Line] = Field(default=[], max_length=3)
    key_messages: list[Line] = Field(default=[], max_length=4)
    offer: str | None = Field(default=None, max_length=300)
    landing_pages: list[Url] = Field(default=[], max_length=6)


class LockedCopy(CamelModel):
    kind: TextKind
    ad_group: str | None = Field(default=None, max_length=100)
    texts: list[Line] = Field(default=[], max_length=60)


class CopyRequest(CamelModel):
    brand: str = Field(min_length=1, max_length=200)
    language: LanguageCode
    campaign_types: list[CampaignType] = Field(min_length=1, max_length=3)
    brief: CopyBrief
    products: list[CopyProduct] = Field(default=[], max_length=12)
    keywords: list[Annotated[str, Field(max_length=100)]] = Field(default=[], max_length=20)
    locked: list[LockedCopy] = Field(default=[], max_length=100)
    brand_kit: dict[str, Any] | None = None
    memory: list[Annotated[str, Field(max_length=500)]] = Field(default=[], max_length=10)


class DraftKeyword(CamelModel):
    text: str
    match: str


class DraftAdGroup(CamelModel):
    name: str
    product_ids: list[str]
    keywords: list[DraftKeyword]
    headlines: list[str]
    descriptions: list[str]
    path1: str
    path2: str


class DraftSearch(CamelModel):
    ad_groups: list[DraftAdGroup]


class DraftDemandGen(CamelModel):
    headlines: list[str]
    long_headlines: list[str]
    descriptions: list[str]
    call_to_action: str


class DraftTitle(CamelModel):
    product_id: str
    title: str


class DraftTitles(CamelModel):
    titles: list[DraftTitle]


class Keyword(CamelModel):
    text: str
    match: Literal["broad", "phrase", "exact"]


class AdGroupCopy(CamelModel):
    name: str
    product_ids: list[str]
    keywords: list[Keyword]
    headlines: list[str]
    descriptions: list[str]
    path1: str | None = None
    path2: str | None = None
    final_url: str | None = None


class DemandGenCopy(CamelModel):
    headlines: list[str]
    long_headlines: list[str]
    descriptions: list[str]
    business_name: str
    call_to_action: str
    final_url: str | None = None


class ShoppingTitle(CamelModel):
    product_id: str
    title: str


class CopySet(CamelModel):
    ad_groups: list[AdGroupCopy] = []
    demand_gen: DemandGenCopy | None = None
    shopping_titles: list[ShoppingTitle] = []


class CopyResponse(CamelModel):
    assets: CopySet
    model: str
    prompt_version: str
    usage: Usage | None = None


class AssetText(CamelModel):
    ad_group: str | None = Field(default=None, max_length=100)
    product_ids: list[ProductKey] = Field(default=[], max_length=12)
    keywords: list[Keyword] = Field(default=[], max_length=50)
    headlines: list[Line] = Field(default=[], max_length=15)
    long_headlines: list[Line] = Field(default=[], max_length=5)
    descriptions: list[Line] = Field(default=[], max_length=5)
    path1: str | None = Field(default=None, max_length=50)
    path2: str | None = Field(default=None, max_length=50)
    business_name: str | None = Field(default=None, max_length=100)
    call_to_action: str | None = Field(default=None, max_length=40)
    title: str | None = Field(default=None, max_length=300)
    current_title: str | None = Field(default=None, max_length=300)


class CopyInstructionRequest(CamelModel):
    brand: str = Field(min_length=1, max_length=200)
    language: LanguageCode
    instruction: str = Field(min_length=2, max_length=500)
    kind: TextKind
    current: AssetText
    brief: CopyBrief
    products: list[CopyProduct] = Field(default=[], max_length=12)
    brand_kit: dict[str, Any] | None = None


class DraftRevision(CamelModel):
    keywords: list[DraftKeyword]
    headlines: list[str]
    long_headlines: list[str]
    descriptions: list[str]
    path1: str
    path2: str
    call_to_action: str
    title: str
    summary: str


class CopyInstructionResponse(CamelModel):
    asset: AssetText
    summary: str
    model: str
    prompt_version: str
    usage: Usage | None = None
