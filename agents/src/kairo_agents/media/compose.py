import re
import tempfile
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageOps

from kairo_agents.media.captions import HEIGHT, WIDTH, Caption, copy_fonts, document
from kairo_agents.media.ffmpeg import run_ffmpeg

FPS = 30
END_CARD_SECONDS = 2.5
LOGO_SHARE = 0.6
HEX = re.compile(r"^#[0-9A-Fa-f]{6}$")


@dataclass(frozen=True)
class Composed:
    video: bytes
    voiceover: bytes
    seconds: float


def hex_colour(value: str | None, fallback: str = "#111111") -> tuple[int, int, int]:
    chosen = value if value and HEX.match(value) else fallback
    return int(chosen[1:3], 16), int(chosen[3:5], 16), int(chosen[5:7], 16)


def end_card(logo: bytes | None, background: str | None) -> bytes:
    card = Image.new("RGB", (WIDTH, HEIGHT), hex_colour(background))
    if logo:
        try:
            with Image.open(BytesIO(logo)) as source:
                mark = ImageOps.exif_transpose(source).convert("RGBA")
        except Exception:
            mark = None
        if mark is not None:
            mark.thumbnail((round(WIDTH * LOGO_SHARE), round(WIDTH * LOGO_SHARE)))
            card.paste(mark, ((WIDTH - mark.width) // 2, (HEIGHT - mark.height) // 2), mark)
    output = BytesIO()
    card.save(output, "PNG")
    return output.getvalue()


def body_seconds(clip_seconds: list[float], seconds: float) -> float:
    return round(min(seconds - END_CARD_SECONDS, sum(clip_seconds)), 2)


def video_filter(clips: int, body: float) -> str:
    scaled = [
        f"[{index}:v]scale={WIDTH}:{HEIGHT}:force_original_aspect_ratio=increase,"
        f"crop={WIDTH}:{HEIGHT},setsar=1,fps={FPS},format=yuv420p[c{index}]"
        for index in range(clips)
    ]
    joined = "".join(f"[c{index}]" for index in range(clips))
    card = clips
    return ";".join(
        [
            *scaled,
            f"{joined}concat=n={clips}:v=1:a=0,trim=duration={body},setpts=PTS-STARTPTS[body]",
            f"[{card}:v]scale={WIDTH}:{HEIGHT},setsar=1,fps={FPS},format=yuv420p[card]",
            "[body][card]concat=n=2:v=1:a=0,ass=captions.ass:fontsdir=fonts:shaping=complex[out]",
        ]
    )


def video_args(clips: int, body: float) -> list[str]:
    inputs: list[str] = []
    for index in range(clips):
        inputs += ["-i", f"clip{index}.mp4"]
    inputs += ["-loop", "1", "-framerate", str(FPS), "-t", str(END_CARD_SECONDS)]
    inputs += ["-i", "card.png", "-i", "voiceover.wav"]
    total = round(body + END_CARD_SECONDS, 2)
    return [
        *inputs,
        "-filter_complex_script",
        "graph.txt",
        "-map",
        "[out]",
        "-map",
        f"{clips + 1}:a",
        "-c:v",
        "libx264",
        "-preset",
        "veryfast",
        "-crf",
        "21",
        "-pix_fmt",
        "yuv420p",
        "-c:a",
        "aac",
        "-b:a",
        "160k",
        "-t",
        str(total),
        "-movflags",
        "+faststart",
        "video.mp4",
    ]


def voiceover_args() -> list[str]:
    return ["-i", "voiceover.wav", "-c:a", "libmp3lame", "-b:a", "128k", "voiceover.mp3"]


async def compose(
    clips: list[tuple[bytes, float]],
    voiceover_wav: bytes,
    captions: list[Caption],
    script: str,
    seconds: float,
    logo: bytes | None,
    background: str | None,
) -> Composed:
    body = body_seconds([length for _, length in clips], seconds)
    with tempfile.TemporaryDirectory(prefix="kairo-video-") as folder:
        work = Path(folder)
        for index, (data, _) in enumerate(clips):
            (work / f"clip{index}.mp4").write_bytes(data)
        (work / "card.png").write_bytes(end_card(logo, background))
        (work / "voiceover.wav").write_bytes(voiceover_wav)
        (work / "captions.ass").write_text(document(captions, script), encoding="utf-8")
        (work / "graph.txt").write_text(video_filter(len(clips), body), encoding="utf-8")
        copy_fonts(work / "fonts")
        await run_ffmpeg(video_args(len(clips), body), work)
        await run_ffmpeg(voiceover_args(), work)
        return Composed(
            video=(work / "video.mp4").read_bytes(),
            voiceover=(work / "voiceover.mp3").read_bytes(),
            seconds=round(body + END_CARD_SECONDS, 2),
        )
