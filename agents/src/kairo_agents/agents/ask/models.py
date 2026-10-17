from typing import Literal

from pydantic import BaseModel, ConfigDict, Field


class Target(BaseModel):
    model_config = ConfigDict(populate_by_name=True, extra="forbid")
    kind: str
    id: str | None = None
    campaign_id: str | None = Field(default=None, alias="campaignId")


class Turn(BaseModel):
    role: Literal["user", "model"]
    text: str = Field(max_length=12000)


class AskRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True, extra="forbid")
    brand_id: str = Field(alias="brandId", pattern=r"^[0-9a-fA-F-]{36}$")
    message_id: str = Field(alias="messageId", pattern=r"^[0-9a-fA-F-]{36}$")
    token: str = Field(pattern=r"^[0-9A-F]{64}$", repr=False)
    question: str = Field(min_length=1, max_length=4000)
    context: Target
    history: list[Turn] = Field(default_factory=list, max_length=20)
    now: str


class AskResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    model: str
    prompt_version: str = Field(alias="promptVersion")
