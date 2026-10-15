from typing import Annotated, Any

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


class TriggerBriefResponse(CamelModel):
    brief: DraftedBrief
    model: str
    prompt_version: str
