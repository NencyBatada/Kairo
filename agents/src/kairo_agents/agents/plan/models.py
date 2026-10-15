from typing import Annotated, Any, Literal

from pydantic import Field

from kairo_agents.agents.brand_kit.models import CamelModel

RegionName = Annotated[str, Field(min_length=1, max_length=100)]


class BriefTrigger(CamelModel):
    source: str = Field(max_length=20)
    summary: str = Field(min_length=1, max_length=500)
    regions: list[RegionName] = Field(min_length=1, max_length=40)
    until: str = Field(max_length=40)


class BriefProduct(CamelModel):
    id: str = Field(pattern=r"^p[0-9]{1,2}$")
    title: str = Field(min_length=1, max_length=150)
    price: float | None = Field(default=None, ge=0)
    currency: str | None = Field(default=None, max_length=3)


class TriggerBriefRequest(CamelModel):
    brand: str = Field(max_length=200)
    market: str = Field(max_length=100)
    category: str = Field(min_length=1, max_length=750)
    trigger: BriefTrigger
    products: list[BriefProduct] = Field(min_length=1, max_length=12)
    brand_kit: dict[str, Any] | None = None


class DraftedBrief(CamelModel):
    title: str
    goal: str
    audience: str
    key_message: str
    offer: str | None = None
    regions: list[str]
    product_ids: list[str]
    channels: list[str]
    timing: str


class Usage(CamelModel):
    input_tokens: int | None = None
    output_tokens: int | None = None


class TriggerBriefResponse(CamelModel):
    brief: DraftedBrief
    model: str
    prompt_version: str
    usage: Usage | None = None


CampaignType = Literal["search", "shopping", "demand_gen"]
Kpi = Literal["orders", "conversions", "revenue"]
IsoDate = Annotated[str, Field(pattern=r"^\d{4}-\d{2}-\d{2}$")]
LanguageCode = Annotated[str, Field(pattern=r"^[a-z]{2}$")]
ShortText = Annotated[str, Field(min_length=1, max_length=200)]


class RegionDemand(CamelModel):
    name: RegionName
    last_month_searches: int | None = Field(default=None, ge=0)
    growth: float | None = None


class BriefOpportunity(CamelModel):
    topic: str = Field(min_length=1, max_length=200)
    category: str = Field(min_length=1, max_length=750)
    source: str = Field(max_length=20)
    summary: str = Field(min_length=1, max_length=500)
    keywords: list[Annotated[str, Field(max_length=100)]] = Field(default=[], max_length=20)
    last_month_searches: int | None = Field(default=None, ge=0)
    growth: float | None = None
    regions: list[RegionDemand] = Field(min_length=1, max_length=40)


class SmartBriefProduct(CamelModel):
    id: str = Field(pattern=r"^p[0-9]{1,2}$")
    title: str = Field(min_length=1, max_length=150)
    price: float | None = Field(default=None, ge=0)
    currency: str | None = Field(default=None, max_length=3)


class BudgetGuide(CamelModel):
    low: float = Field(gt=0)
    high: float = Field(gt=0)
    suggested: float = Field(gt=0)
    currency: str = Field(min_length=3, max_length=3)
    days: int = Field(ge=1, le=90)
    basis: str = Field(max_length=500)


class DateGuide(CamelModel):
    today: IsoDate
    suggested_start: IsoDate
    suggested_end: IsoDate
    latest_end: IsoDate | None = None


class BriefSeed(CamelModel):
    title: str | None = Field(default=None, max_length=200)
    goal: str | None = Field(default=None, max_length=500)
    audience: str | None = Field(default=None, max_length=500)
    key_message: str | None = Field(default=None, max_length=500)
    offer: str | None = Field(default=None, max_length=300)


class SmartBriefRequest(CamelModel):
    brand: str = Field(max_length=200)
    market: str = Field(max_length=100)
    opportunity: BriefOpportunity
    products: list[SmartBriefProduct] = Field(min_length=1, max_length=12)
    budget: BudgetGuide
    dates: DateGuide
    languages: list[LanguageCode] = Field(min_length=1, max_length=12)
    seed: BriefSeed | None = None
    memory: list[Annotated[str, Field(max_length=500)]] = Field(default=[], max_length=20)
    brand_kit: dict[str, Any] | None = None


class FieldReasons(CamelModel):
    objective: str = ""
    kpi: str = ""
    campaign_types: str = ""
    budget: str = ""
    dates: str = ""
    regions: str = ""
    audiences: str = ""
    key_messages: str = ""
    products: str = ""
    languages: str = ""


class DraftedSmartBrief(CamelModel):
    title: str
    objective: str
    kpi: str
    campaign_types: list[str]
    budget: float
    start_date: str
    end_date: str
    regions: list[str]
    audiences: list[str]
    key_messages: list[str]
    product_ids: list[str]
    languages: list[str]
    reasons: FieldReasons


class SmartBrief(CamelModel):
    title: str
    objective: str
    kpi: Kpi
    campaign_types: list[CampaignType]
    budget: float
    start_date: str
    end_date: str
    regions: list[str]
    audiences: list[str]
    key_messages: list[str]
    product_ids: list[str]
    languages: list[str]
    reasons: FieldReasons


class SmartBriefResponse(CamelModel):
    brief: SmartBrief
    model: str
    prompt_version: str
    usage: Usage | None = None


class CurrentBrief(CamelModel):
    title: str = Field(max_length=200)
    objective: str = Field(max_length=500)
    kpi: str = Field(max_length=20)
    campaign_types: list[str] = Field(max_length=3)
    budget: float | None = Field(default=None, ge=0)
    currency: str = Field(min_length=3, max_length=3)
    start_date: IsoDate | None = None
    end_date: IsoDate | None = None
    regions: list[RegionName] = Field(max_length=40)
    audiences: list[ShortText] = Field(max_length=5)
    key_messages: list[ShortText] = Field(max_length=5)
    product_ids: list[str] = Field(max_length=12)
    languages: list[str] = Field(max_length=12)


class InstructionOptions(CamelModel):
    regions: list[RegionName] = Field(min_length=1, max_length=60)
    products: list[SmartBriefProduct] = Field(min_length=1, max_length=12)
    languages: list[LanguageCode] = Field(min_length=1, max_length=12)


class BriefInstructionRequest(CamelModel):
    brand: str = Field(max_length=200)
    today: IsoDate
    instruction: str = Field(min_length=2, max_length=500)
    brief: CurrentBrief
    options: InstructionOptions
    brand_kit: dict[str, Any] | None = None


class ProposedBrief(CamelModel):
    title: str | None = None
    objective: str | None = None
    kpi: str | None = None
    campaign_types: list[str] | None = None
    budget: float | None = None
    start_date: str | None = None
    end_date: str | None = None
    regions: list[str] | None = None
    audiences: list[str] | None = None
    key_messages: list[str] | None = None
    product_ids: list[str] | None = None
    languages: list[str] | None = None
    reasons: FieldReasons
    summary: str = ""


class BriefInstructionResponse(CamelModel):
    changes: ProposedBrief
    model: str
    prompt_version: str
    usage: Usage | None = None
