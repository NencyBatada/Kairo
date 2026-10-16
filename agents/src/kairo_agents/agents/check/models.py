from typing import Annotated, Any, Literal

from pydantic import Field, model_validator

from kairo_agents.agents.brand_kit.models import CamelModel
from kairo_agents.agents.create.media_models import (
    MAX_CLIP_BYTES,
    Bytes,
    Photo,
    ReferenceProduct,
)
from kairo_agents.agents.create.models import AssetText, CopyBrief, CopyProduct, TextKind
from kairo_agents.agents.plan.models import LanguageCode, Usage
from kairo_agents.media.formats import FormatName

Medium = Literal["text", "image", "video"]
RuleId = Annotated[
    str, Field(pattern=r"^(?:[a-z]+\.[a-z_]+|custom\.[A-Za-z0-9_-]{1,40})$", max_length=60)
]
CustomRuleId = Annotated[str, Field(pattern=r"^custom\.[A-Za-z0-9_-]{1,40}$", max_length=60)]
AssetId = Annotated[str, Field(min_length=1, max_length=64)]
MAX_VIDEO_BYTES = MAX_CLIP_BYTES


class CheckRule(CamelModel):
    id: RuleId
    name: str = Field(min_length=1, max_length=100)
    description: str = Field(default="", max_length=500)
    applies_to: list[Medium] = Field(
        default_factory=lambda: ["text", "image", "video"], min_length=1, max_length=3
    )


class CustomRule(CamelModel):
    id: CustomRuleId
    text: str = Field(min_length=1, max_length=200)
    applies_to: Literal["text", "image", "video", "all"] = "all"


class CheckRequestBase(CamelModel):
    brand: str = Field(min_length=1, max_length=200)
    language: LanguageCode = "en"
    brief: CopyBrief
    products: list[CopyProduct] = Field(default_factory=list, max_length=12)
    brand_kit: dict[str, Any] | None = None
    rules: list[CheckRule] = Field(default_factory=list, max_length=30)
    custom_rules: list[CustomRule] = Field(default_factory=list, max_length=20)


class TextAsset(CamelModel):
    id: AssetId
    kind: TextKind
    content: AssetText


class CopyCheckRequest(CheckRequestBase):
    assets: list[TextAsset] = Field(min_length=1, max_length=60)

    @model_validator(mode="after")
    def unique(self) -> "CopyCheckRequest":
        if len({asset.id for asset in self.assets}) != len(self.assets):
            raise ValueError("asset ids must be unique")
        return self


class ImageCheckRequest(CheckRequestBase):
    asset_id: AssetId
    format: FormatName | None = None
    image: Photo
    references: list[ReferenceProduct] = Field(default_factory=list, max_length=3)


class VideoInput(CamelModel):
    mime_type: Literal["video/mp4"] = "video/mp4"
    data: Bytes

    @model_validator(mode="after")
    def small_enough(self) -> "VideoInput":
        if not self.data or len(self.data) > MAX_VIDEO_BYTES:
            raise ValueError("video must be between 1 byte and 60 MB")
        return self


class CheckScriptLine(CamelModel):
    voiceover: str = Field(default="", max_length=300)
    caption: str = Field(default="", max_length=120)
    start: float = Field(default=0, ge=0)
    end: float = Field(default=0, ge=0)


class VideoCheckRequest(CheckRequestBase):
    asset_id: AssetId
    video: VideoInput
    seconds: float | None = Field(default=None, gt=0, le=120)
    script: list[CheckScriptLine] = Field(default_factory=list, max_length=12)
    references: list[ReferenceProduct] = Field(default_factory=list, max_length=3)
    logo: Photo | None = None


class Finding(CamelModel):
    rule: str
    field: str | None = None
    index: int | None = None
    at: float | None = None
    message: str
    evidence: str | None = None


class AssetFindings(CamelModel):
    asset_id: str
    findings: list[Finding]


class CheckResponse(CamelModel):
    results: list[AssetFindings]
    model: str
    prompt_version: str
    usage: Usage | None = None


class DraftFinding(CamelModel):
    asset_id: str
    rule: str
    field: str
    index: int
    message: str
    evidence: str


class DraftReview(CamelModel):
    findings: list[DraftFinding]


class DraftMediaFinding(CamelModel):
    rule: str
    field: str
    index: int
    message: str
    evidence: str


class DraftImageReview(CamelModel):
    on_image_text: list[str]
    findings: list[DraftMediaFinding]


class DraftVideoReview(CamelModel):
    findings: list[DraftMediaFinding]
