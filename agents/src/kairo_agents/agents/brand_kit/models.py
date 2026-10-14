from typing import Literal

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class Brand(CamelModel):
    name: str = Field(max_length=200)
    currency: str = Field(default="", max_length=3)


class Page(CamelModel):
    id: str = Field(pattern=r"^p[0-9]{1,2}$")
    url: str = Field(max_length=2000)
    title: str = Field(default="", max_length=300)
    description: str = Field(default="", max_length=500)
    headings: list[str] = Field(default_factory=list, max_length=30)
    text: str = Field(default="", max_length=6000)


class Ad(CamelModel):
    id: str = Field(pattern=r"^a[0-9]{1,2}$")
    headlines: list[str] = Field(default_factory=list, max_length=20)
    descriptions: list[str] = Field(default_factory=list, max_length=10)


class ColourCandidate(CamelModel):
    hex: str = Field(pattern=r"^#[0-9A-F]{6}$")
    count: int = Field(ge=1)
    found_in: list[str] = Field(default_factory=list, alias="from", max_length=10)


class LogoCandidate(CamelModel):
    id: str = Field(pattern=r"^l[0-9]{1,2}$")
    url: str = Field(max_length=2000)
    found_in: str = Field(alias="from", max_length=50)
    alt: str = Field(default="", max_length=300)


class DraftRequest(CamelModel):
    brand: Brand
    pages: list[Page] = Field(default_factory=list, max_length=9)
    ads: list[Ad] = Field(default_factory=list, max_length=10)
    colour_candidates: list[ColourCandidate] = Field(default_factory=list, max_length=12)
    logo_candidates: list[LogoCandidate] = Field(default_factory=list, max_length=10)


Role = Literal["primary", "secondary", "accent", "background", "text"]


class DraftVoice(CamelModel):
    summary: str
    traits: list[str]
    dos: list[str]
    donts: list[str]
    examples: list[str]
    sources: list[str]


class DraftColour(CamelModel):
    hex: str
    name: str
    role: Role
    sources: list[str]


class DraftLogo(CamelModel):
    candidate_id: str


class DraftMessage(CamelModel):
    text: str
    sources: list[str]


class DraftCategory(CamelModel):
    name: str
    sources: list[str]


class DraftAnswer(CamelModel):
    voice: DraftVoice
    colours: list[DraftColour]
    logo: DraftLogo | None
    key_messages: list[DraftMessage]
    product_categories: list[DraftCategory]


class DraftResponse(DraftAnswer):
    model: str
    prompt_version: str
