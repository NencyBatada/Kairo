from dataclasses import dataclass
from io import BytesIO
from typing import Literal

from PIL import Image, ImageOps

FormatName = Literal["landscape", "square", "portrait", "vertical"]


@dataclass(frozen=True)
class Format:
    name: FormatName
    width: int
    height: int
    aspect: str


FORMATS: dict[str, Format] = {
    item.name: item
    for item in (
        Format("landscape", 1200, 628, "16:9"),
        Format("square", 1200, 1200, "1:1"),
        Format("portrait", 960, 1200, "3:4"),
        Format("vertical", 1080, 1920, "9:16"),
    )
}

MAX_PIXELS = 40_000_000


class BadImage(Exception):
    pass


def crop_box(width: int, height: int, target_width: int, target_height: int) -> tuple[int, ...]:
    target = target_width / target_height
    if width / height > target:
        cropped = max(1, round(height * target))
        left = (width - cropped) // 2
        return left, 0, left + cropped, height
    cropped = max(1, round(width / target))
    top = (height - cropped) // 2
    return 0, top, width, top + cropped


def fit(data: bytes, format: Format) -> bytes:
    try:
        with Image.open(BytesIO(data)) as source:
            if source.width * source.height > MAX_PIXELS:
                raise BadImage("too_large")
            image = ImageOps.exif_transpose(source).convert("RGB")
    except BadImage:
        raise
    except Exception as error:
        raise BadImage("unreadable") from error
    box = crop_box(image.width, image.height, format.width, format.height)
    fitted = image.crop(box).resize((format.width, format.height), Image.Resampling.LANCZOS)
    output = BytesIO()
    fitted.save(output, "JPEG", quality=90, optimize=True)
    return output.getvalue()
