from typing import Annotated

from pydantic import Field

from kairo_agents.agents.brand_kit.models import CamelModel

Title = Annotated[str, Field(max_length=150)]
KeywordText = Annotated[str, Field(max_length=200)]


class Market(CamelModel):
    country: str = Field(max_length=100)
    language: str = Field(default="", max_length=20)


class Keyword(CamelModel):
    text: str = Field(min_length=1, max_length=200)
    last_month_searches: int = Field(ge=0)
    growth: float


class Category(CamelModel):
    name: str = Field(min_length=1, max_length=750)
    in_stock: int = Field(ge=0)
    sample_titles: list[Title] = Field(default_factory=list, max_length=3)


class Dismissal(CamelModel):
    topic: str = Field(max_length=200)
    reason: str = Field(max_length=20)


class MatchRequest(CamelModel):
    brand: str = Field(max_length=200)
    market: Market
    keywords: list[Keyword] = Field(min_length=1, max_length=200)
    categories: list[Category] = Field(min_length=1, max_length=100)
    dismissed: list[Dismissal] = Field(default_factory=list, max_length=30)
    watched: list[Annotated[str, Field(max_length=100)]] = Field(
        default_factory=list, max_length=200
    )


class MatchTopic(CamelModel):
    name: str
    keywords: list[str]
    categories: list[str]
    fit: float
    fit_reason: str


class MatchAnswer(CamelModel):
    topics: list[MatchTopic]


class MatchResponse(MatchAnswer):
    model: str
    prompt_version: str


class ExplainTopic(CamelModel):
    id: str = Field(pattern=r"^t[0-9]{1,2}$")
    name: str = Field(min_length=1, max_length=200)
    keywords: list[KeywordText] = Field(default_factory=list, max_length=5)
    growth: float
    yoy: float | None = None


class ExplainRequest(CamelModel):
    brand: str = Field(max_length=200)
    market: Market
    topics: list[ExplainTopic] = Field(min_length=1, max_length=20)


class Source(CamelModel):
    title: str
    url: str


class Reason(CamelModel):
    text: str
    sources: list[Source]


class ExplainedTopic(CamelModel):
    id: str
    reasons: list[Reason]


class ExplainResponse(CamelModel):
    topics: list[ExplainedTopic]
    model: str
    prompt_version: str
