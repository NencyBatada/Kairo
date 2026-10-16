from typing import Annotated, Any, Literal

from pydantic import Field

from kairo_agents.agents.brand_kit.models import CamelModel
from kairo_agents.agents.plan.models import Usage

Outcome = Literal["worked", "no_effect", "inconclusive"]


class LearnProduct(CamelModel):
    id: str = Field(max_length=64)
    title: str = Field(max_length=300)
    category: str | None = Field(default=None, max_length=750)


class LearnCampaign(CamelModel):
    title: str = Field(max_length=200)
    kpi: str = Field(max_length=30)
    objective: str | None = Field(default=None, max_length=500)
    audiences: list[Annotated[str, Field(max_length=300)]] = Field(default=[], max_length=6)
    key_messages: list[Annotated[str, Field(max_length=300)]] = Field(default=[], max_length=6)
    languages: list[Annotated[str, Field(max_length=8)]] = Field(default=[], max_length=12)
    products: list[LearnProduct] = Field(default=[], max_length=12)


class LearnResult(CamelModel):
    outcome: Outcome
    lift: float | None = None
    low: float | None = None
    high: float | None = None
    extra: float | None = None
    extra_low: float | None = None
    extra_high: float | None = None
    cost_per_extra: float | None = None
    spend: float = 0
    results: float = 0


class LearnRegion(CamelModel):
    name: str = Field(max_length=100)
    test: bool
    conclusive: bool = False
    lift: float | None = None
    low: float | None = None
    high: float | None = None
    extra: float | None = None


class LearnAd(CamelModel):
    id: str = Field(pattern=r"^[A-Za-z0-9:_-]{1,80}$")
    campaign_type: str = Field(max_length=20)
    language: str | None = Field(default=None, max_length=8)
    headlines: list[Annotated[str, Field(max_length=300)]] = Field(default=[], max_length=20)
    descriptions: list[Annotated[str, Field(max_length=300)]] = Field(default=[], max_length=10)
    visual: str | None = Field(default=None, max_length=1000)
    impressions: int = Field(default=0, ge=0)
    clicks: int = Field(default=0, ge=0)
    conversions: float = Field(default=0, ge=0)
    cost: float = Field(default=0, ge=0)


class LearnRequest(CamelModel):
    brand: str = Field(max_length=200)
    currency: str = Field(default="", max_length=3)
    campaign: LearnCampaign
    result: LearnResult
    regions: list[LearnRegion] = Field(default=[], max_length=120)
    ads: list[LearnAd] = Field(default=[], max_length=100)


class DraftLesson(CamelModel):
    kind: str
    outcome: str
    title: str
    lesson: str
    ad_ids: list[str] = []
    regions: list[str] = []
    evidence: str = ""


class DraftLessons(CamelModel):
    lessons: list[DraftLesson]


class Lesson(CamelModel):
    kind: str
    outcome: str
    title: str
    lesson: str
    ad_ids: list[str]
    regions: list[str]
    languages: list[str]
    evidence: dict[str, Any]


class LearnResponse(CamelModel):
    lessons: list[Lesson]
    model: str | None = None
    prompt_version: str
    usage: Usage | None = None
