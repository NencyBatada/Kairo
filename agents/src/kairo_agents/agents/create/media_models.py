import base64
import binascii
from typing import Annotated, Any, Literal

from pydantic import BeforeValidator, Field, PlainSerializer, model_validator

from kairo_agents.agents.brand_kit.models import CamelModel
from kairo_agents.agents.plan.models import Usage
from kairo_agents.media.formats import FormatName

MAX_PHOTO_BYTES = 10 * 1024 * 1024
MAX_CLIP_BYTES = 60 * 1024 * 1024
MAX_GENERATIONS = 24


def _decode(value: Any) -> bytes:
    if isinstance(value, bytes):
        return value
    if not isinstance(value, str):
        raise ValueError("expected base64 text")
    try:
        return base64.b64decode(value, validate=True)
    except binascii.Error as error:
        raise ValueError("invalid base64") from error


def _encode(value: bytes) -> str:
    return base64.b64encode(value).decode("ascii")


Bytes = Annotated[bytes, BeforeValidator(_decode), PlainSerializer(_encode, return_type=str)]
ProductId = Annotated[str, Field(pattern=r"^p[0-9]{1,2}$")]
LanguageCode = Annotated[str, Field(pattern=r"^[a-z]{2}$")]
ImageType = Literal["image/jpeg", "image/png", "image/webp"]


class Photo(CamelModel):
    mime_type: ImageType
    data: Bytes

    @model_validator(mode="after")
    def small_enough(self) -> "Photo":
        if not self.data or len(self.data) > MAX_PHOTO_BYTES:
            raise ValueError("photo must be between 1 byte and 10 MB")
        return self


class MediaBrief(CamelModel):
    title: str = Field(max_length=200)
    objective: str = Field(default="", max_length=500)
    key_messages: list[Annotated[str, Field(max_length=300)]] = Field(
        default_factory=list, max_length=6
    )
    audiences: list[Annotated[str, Field(max_length=300)]] = Field(
        default_factory=list, max_length=6
    )


class ReferenceProduct(CamelModel):
    id: ProductId
    title: str = Field(max_length=300)
    description: str = Field(default="", max_length=2000)
    photos: list[Photo] = Field(min_length=1, max_length=3)


class Shot(CamelModel):
    id: str = Field(pattern=r"^s[0-9]{1,2}$")
    product_id: ProductId
    style: Literal["product", "lifestyle"]
    formats: list[FormatName] = Field(min_length=1, max_length=4)


class ImagesRequest(CamelModel):
    brief: MediaBrief
    brand_kit: dict[str, Any] | None = None
    products: list[ReferenceProduct] = Field(min_length=1, max_length=6)
    shots: list[Shot] = Field(min_length=1, max_length=12)
    instruction: str | None = Field(default=None, max_length=500)

    @model_validator(mode="after")
    def consistent(self) -> "ImagesRequest":
        products = {product.id for product in self.products}
        if any(shot.product_id not in products for shot in self.shots):
            raise ValueError("every shot must name a product in the request")
        if len({shot.id for shot in self.shots}) != len(self.shots):
            raise ValueError("shot ids must be unique")
        if sum(len(set(shot.formats)) for shot in self.shots) > MAX_GENERATIONS:
            raise ValueError("too many images in one request")
        return self


class GeneratedImage(CamelModel):
    shot_id: str
    product_id: str
    style: str
    format: FormatName
    width: int
    height: int
    mime_type: Literal["image/jpeg"] = "image/jpeg"
    data: Bytes
    prompt: str


class ImageFailure(CamelModel):
    shot_id: str
    format: FormatName
    reason: Literal["model_failed", "no_image", "bad_image"]


class ImagesResponse(CamelModel):
    images: list[GeneratedImage]
    failures: list[ImageFailure]
    model: str
    prompt_version: str
    usage: Usage | None = None


class FootageProduct(CamelModel):
    id: ProductId
    title: str = Field(max_length=300)


class Frame(CamelModel):
    product_id: ProductId | None = None
    mime_type: ImageType
    data: Bytes

    @model_validator(mode="after")
    def small_enough(self) -> "Frame":
        if not self.data or len(self.data) > MAX_PHOTO_BYTES:
            raise ValueError("frame must be between 1 byte and 10 MB")
        return self


class FootageRequest(CamelModel):
    brief: MediaBrief
    brand_kit: dict[str, Any] | None = None
    products: list[FootageProduct] = Field(default_factory=list, max_length=6)
    frames: list[Frame] = Field(min_length=1, max_length=3)
    clips: int = Field(default=2, ge=1, le=3)
    clip_seconds: Literal[4, 6, 8] = 8
    instruction: str | None = Field(default=None, max_length=500)


class Scene(CamelModel):
    frame: int = Field(ge=0)
    prompt: str


class ScenePlan(CamelModel):
    scenes: list[Scene]


class FootageClip(CamelModel):
    index: int
    prompt: str
    mime_type: Literal["video/mp4"] = "video/mp4"
    data: Bytes
    seconds: float


class FootageResponse(CamelModel):
    clips: list[FootageClip]
    model: str
    planner_model: str
    prompt_version: str
    usage: Usage | None = None


class ClipInput(CamelModel):
    mime_type: Literal["video/mp4"] = "video/mp4"
    data: Bytes
    seconds: float = Field(gt=0, le=8)

    @model_validator(mode="after")
    def small_enough(self) -> "ClipInput":
        if not self.data or len(self.data) > MAX_CLIP_BYTES:
            raise ValueError("clip must be between 1 byte and 60 MB")
        return self


class ScriptLine(CamelModel):
    voiceover: str = Field(max_length=300)
    caption: str = Field(max_length=120)


class VoiceoverScript(CamelModel):
    lines: list[ScriptLine]


class VideoRequest(CamelModel):
    brief: MediaBrief
    brand_kit: dict[str, Any] | None = None
    business_name: str = Field(default="", max_length=100)
    language: LanguageCode = "en"
    clips: list[ClipInput] = Field(min_length=1, max_length=3)
    logo: Photo | None = None
    background: str | None = Field(default=None, pattern=r"^#[0-9A-Fa-f]{6}$")
    seconds: int = Field(default=15, ge=8, le=20)
    script: list[ScriptLine] | None = Field(default=None, max_length=6)
    instruction: str | None = Field(default=None, max_length=500)


class TimedLine(CamelModel):
    voiceover: str
    caption: str
    start: float
    end: float


class MediaOut(CamelModel):
    mime_type: str
    data: Bytes
    seconds: float


class VideoResponse(CamelModel):
    video: MediaOut
    voiceover: MediaOut
    width: int
    height: int
    language: str
    voice: str
    script: list[TimedLine]
    model: str
    prompt_version: str
    usage: Usage | None = None
