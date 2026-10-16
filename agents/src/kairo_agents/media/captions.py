from dataclasses import dataclass
from importlib import resources
from pathlib import Path
from shutil import copyfile

FONT_FILES = ("NotoSans-Bold.ttf", "NotoSansDevanagari-Bold.ttf")
FAMILIES = {"latin": "Noto Sans", "devanagari": "Noto Sans Devanagari"}
WIDTH = 1080
HEIGHT = 1920
LINE_CHARACTERS = 24
DEVANAGARI = ((0x0900, 0x097F), (0xA8E0, 0xA8FF), (0x1CD0, 0x1CFF))


@dataclass(frozen=True)
class Caption:
    start: float
    end: float
    text: str


def is_devanagari(char: str) -> bool:
    return any(start <= ord(char) <= end for start, end in DEVANAGARI)


def tagged(line: str) -> str:
    parts: list[str] = []
    family = None
    for char in line:
        if is_devanagari(char):
            wanted = FAMILIES["devanagari"]
        elif char.isalpha():
            wanted = FAMILIES["latin"]
        else:
            wanted = family
        if wanted is not None and wanted != family:
            parts.append("{\\fn" + wanted + "}")
            family = wanted
        parts.append(char)
    return "".join(parts)


def family_for(script: str) -> str:
    return FAMILIES.get(script, FAMILIES["latin"])


def copy_fonts(target: Path) -> Path:
    target.mkdir(parents=True, exist_ok=True)
    fonts = resources.files("kairo_agents.media") / "fonts"
    for name in FONT_FILES:
        with resources.as_file(fonts / name) as source:
            copyfile(source, target / name)
    return target


def wrap(text: str, width: int = LINE_CHARACTERS) -> list[str]:
    lines: list[str] = []
    current = ""
    for word in text.split():
        candidate = f"{current} {word}".strip()
        if current and len(candidate) > width:
            lines.append(current)
            current = word
        else:
            current = candidate
    if current:
        lines.append(current)
    if len(lines) <= 2:
        return lines
    middle = (len(text.split()) + 1) // 2
    words = text.split()
    return [" ".join(words[:middle]), " ".join(words[middle:])]


def clean(text: str) -> str:
    return " ".join(text.split()).replace("\\", "/").replace("{", "(").replace("}", ")")


def timestamp(seconds: float) -> str:
    centis = max(0, round(seconds * 100))
    hours, centis = divmod(centis, 360000)
    minutes, centis = divmod(centis, 6000)
    whole, centis = divmod(centis, 100)
    return f"{hours}:{minutes:02d}:{whole:02d}.{centis:02d}"


def document(captions: list[Caption], script: str, size: int = 64) -> str:
    family = family_for(script)
    header = [
        "[Script Info]",
        "ScriptType: v4.00+",
        f"PlayResX: {WIDTH}",
        f"PlayResY: {HEIGHT}",
        "WrapStyle: 2",
        "ScaledBorderAndShadow: yes",
        "",
        "[V4+ Styles]",
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, "
        "BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, "
        "BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
        f"Style: Caption,{family},{size},&H00FFFFFF,&H00FFFFFF,&H64000000,&H64000000,"
        "-1,0,0,0,100,100,0,0,3,18,0,2,90,90,360,1",
        "",
        "[Events]",
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text",
    ]
    events = [
        f"Dialogue: 0,{timestamp(caption.start)},{timestamp(caption.end)},Caption,,0,0,0,,"
        + "\\N".join(tagged(line) for line in wrap(clean(caption.text)))
        for caption in captions
        if clean(caption.text) and caption.end > caption.start
    ]
    return "\n".join(header + events) + "\n"
