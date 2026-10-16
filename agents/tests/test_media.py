import asyncio
import subprocess
import wave
from io import BytesIO
from pathlib import Path

import imageio_ffmpeg
import pytest
from PIL import Image

from kairo_agents.media.audio import BadAudio, Pcm, place, read_wav
from kairo_agents.media.captions import (
    Caption,
    clean,
    copy_fonts,
    document,
    family_for,
    timestamp,
    wrap,
)
from kairo_agents.media.compose import (
    END_CARD_SECONDS,
    body_seconds,
    compose,
    end_card,
    hex_colour,
    video_filter,
)
from kairo_agents.media.ffmpeg import ffmpeg_path, run_ffmpeg
from kairo_agents.media.formats import FORMATS, BadImage, crop_box, fit


def picture(width: int, height: int, colour: str = "red", kind: str = "PNG") -> bytes:
    output = BytesIO()
    Image.new("RGB", (width, height), colour).save(output, kind)
    return output.getvalue()


def tone(seconds: float, rate: int = 24000, level: int = 1000) -> bytes:
    output = BytesIO()
    with wave.open(output, "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(rate)
        writer.writeframes(level.to_bytes(2, "little", signed=True) * int(rate * seconds))
    return output.getvalue()


def clip(path: Path, colour: str, seconds: float = 1.0) -> bytes:
    subprocess.run(
        [
            ffmpeg_path(),
            "-loglevel",
            "error",
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"color=c={colour}:s=360x640:d={seconds}:r=24",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            str(path),
        ],
        check=True,
    )
    return path.read_bytes()


@pytest.mark.parametrize(
    ("size", "target", "box"),
    [
        ((2000, 1000), (1000, 1000), (500, 0, 1500, 1000)),
        ((1000, 2000), (1000, 1000), (0, 500, 1000, 1500)),
        ((1000, 1000), (1200, 628), (0, 238, 1000, 761)),
        ((1000, 1000), (1080, 1920), (219, 0, 781, 1000)),
    ],
)
def test_crop_box_keeps_the_centre_at_the_target_ratio(size, target, box) -> None:
    assert crop_box(*size, *target) == box


@pytest.mark.parametrize("name", ["landscape", "square", "portrait", "vertical"])
def test_fit_makes_the_exact_ads_size_as_jpeg(name: str) -> None:
    target = FORMATS[name]

    fitted = fit(picture(1024, 1536), target)

    with Image.open(BytesIO(fitted)) as image:
        assert image.format == "JPEG"
        assert image.size == (target.width, target.height)


def test_ads_sizes_match_google_ads_specs() -> None:
    assert {name: (item.width, item.height) for name, item in FORMATS.items()} == {
        "landscape": (1200, 628),
        "square": (1200, 1200),
        "portrait": (960, 1200),
        "vertical": (1080, 1920),
    }


def test_fit_refuses_data_that_is_not_an_image() -> None:
    with pytest.raises(BadImage):
        fit(b"not an image", FORMATS["square"])


def test_timestamps_use_ass_centiseconds() -> None:
    assert timestamp(0) == "0:00:00.00"
    assert timestamp(75.456) == "0:01:15.46"
    assert timestamp(-1) == "0:00:00.00"


def test_wrap_keeps_captions_to_two_lines() -> None:
    assert wrap("Stay dry this monsoon") == ["Stay dry this monsoon"]
    assert wrap("Waterproof runners that dry in an hour after every rainy commute") == [
        "Waterproof runners that dry in an",
        "hour after every rainy commute",
    ]


def test_clean_removes_ass_override_characters() -> None:
    assert clean("  {\\b1}Bold\\N  claim ") == "(/b1)Bold/N claim"


def test_document_picks_the_font_for_the_script_and_skips_empty_lines() -> None:
    text = document(
        [
            Caption(0.3, 2.0, "बारिश में भी सूखे"),
            Caption(2.0, 2.0, "zero length"),
            Caption(2.5, 3.0, "   "),
        ],
        "devanagari",
    )

    assert "Style: Caption,Noto Sans Devanagari," in text
    assert "Dialogue: 0,0:00:00.30,0:00:02.00,Caption,,0,0,0,,बारिश में भी सूखे" in text
    assert text.count("Dialogue:") == 1
    assert family_for("latin") == "Noto Sans"
    assert family_for("unknown") == "Noto Sans"


def test_place_puts_each_line_at_its_start_and_pads_to_the_length() -> None:
    first = read_wav(tone(0.5))
    second = read_wav(tone(0.5, level=2000))

    placed = read_wav(place([(0.0, first), (1.0, second)], 2.0))

    assert placed.seconds == pytest.approx(2.0)
    assert placed.frames[:2] == (1000).to_bytes(2, "little", signed=True)
    middle = int(0.75 * 24000) * 2
    assert placed.frames[middle : middle + 2] == b"\x00\x00"
    late = int(1.25 * 24000) * 2
    assert placed.frames[late : late + 2] == (2000).to_bytes(2, "little", signed=True)


def test_place_cuts_lines_that_run_past_the_end() -> None:
    placed = read_wav(place([(1.5, read_wav(tone(1.0)))], 2.0))

    assert placed.seconds == pytest.approx(2.0)


def test_place_refuses_mixed_audio_formats() -> None:
    with pytest.raises(BadAudio):
        place([(0, read_wav(tone(0.2))), (0.5, read_wav(tone(0.2, rate=16000)))], 1.0)
    with pytest.raises(BadAudio):
        place([], 1.0)


def test_read_wav_refuses_other_audio() -> None:
    with pytest.raises(BadAudio):
        read_wav(b"ID3 not a wav")


def test_pcm_length_is_in_seconds() -> None:
    assert Pcm(b"\x00\x00" * 24000, 24000, 2, 1).seconds == pytest.approx(1.0)


def test_body_leaves_room_for_the_end_card() -> None:
    assert body_seconds([8, 8], 15) == 15 - END_CARD_SECONDS
    assert body_seconds([4], 15) == 4


def test_video_filter_joins_clips_then_end_card_and_burns_shaped_captions() -> None:
    graph = video_filter(2, 12.5)

    assert "[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920" in graph
    assert "[c0][c1]concat=n=2:v=1:a=0,trim=duration=12.5" in graph
    assert "[2:v]scale=1080:1920" in graph
    assert graph.endswith("ass=captions.ass:fontsdir=fonts:shaping=complex[out]")


def test_hex_colour_falls_back_when_the_colour_is_invalid() -> None:
    assert hex_colour("#1A2b3C") == (26, 43, 60)
    assert hex_colour("blue") == (17, 17, 17)
    assert hex_colour(None) == (17, 17, 17)


def test_end_card_centres_the_logo_on_the_brand_colour() -> None:
    card = end_card(picture(200, 100, "white"), "#003366")

    with Image.open(BytesIO(card)) as image:
        assert image.size == (1080, 1920)
        assert image.getpixel((10, 10)) == (0, 51, 102)
        assert image.getpixel((540, 960)) == (255, 255, 255)


def test_end_card_ignores_a_broken_logo() -> None:
    with Image.open(BytesIO(end_card(b"broken", None))) as image:
        assert image.getpixel((540, 960)) == (17, 17, 17)


def test_ffmpeg_path_can_be_overridden(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("KAIRO_FFMPEG", "/opt/ffmpeg")
    assert ffmpeg_path() == "/opt/ffmpeg"
    monkeypatch.delenv("KAIRO_FFMPEG")
    assert ffmpeg_path() == imageio_ffmpeg.get_ffmpeg_exe()


async def render(work: Path, text: str, script: str, shaping: str) -> bytes:
    (work / "captions.ass").write_text(document([Caption(0, 5, text)], script), "utf-8")
    copy_fonts(work / "fonts")
    output = f"{shaping}.png"
    await run_ffmpeg(
        [
            "-f",
            "lavfi",
            "-i",
            "color=c=black:s=1080x1920",
            "-frames:v",
            "1",
            "-update",
            "1",
            "-vf",
            f"ass=captions.ass:fontsdir=fonts:shaping={shaping}",
            output,
        ],
        work,
    )
    with Image.open(work / output) as image:
        return image.convert("L").tobytes()


def test_hindi_captions_are_shaped(tmp_path: Path) -> None:
    shaped = asyncio.run(render(tmp_path, "किताब हिंदी", "devanagari", "complex"))
    plain = asyncio.run(render(tmp_path, "किताब हिंदी", "devanagari", "simple"))

    assert any(shaped)
    assert shaped != plain


def ink_width(pixels: bytes) -> int:
    columns = [index % 1080 for index, value in enumerate(pixels) if value > 128]
    return max(columns) - min(columns)


def test_hindi_captions_fall_back_to_latin_glyphs(tmp_path: Path) -> None:
    capitals = asyncio.run(render(tmp_path, "KKKKK", "devanagari", "complex"))
    rounds = asyncio.run(render(tmp_path, "ooooo", "devanagari", "complex"))

    assert any(capitals)
    assert ink_width(capitals) > ink_width(rounds) + 10


def test_compose_makes_a_vertical_video_with_voiceover(tmp_path: Path) -> None:
    clips = [
        (clip(tmp_path / "a.mp4", "red"), 1.0),
        (clip(tmp_path / "b.mp4", "blue"), 1.0),
    ]
    voiceover = place([(0.3, read_wav(tone(1.0)))], 4.5)

    composed = asyncio.run(
        compose(
            clips,
            voiceover,
            [Caption(0.3, 1.3, "Stay dry"), Caption(1.5, 2.0, "बारिश")],
            "devanagari",
            15,
            picture(300, 300, "white"),
            "#204080",
        )
    )

    (tmp_path / "out.mp4").write_bytes(composed.video)
    frames, seconds = imageio_ffmpeg.count_frames_and_secs(str(tmp_path / "out.mp4"))
    assert composed.seconds == pytest.approx(2.0 + END_CARD_SECONDS)
    assert seconds == pytest.approx(composed.seconds, abs=0.1)
    assert frames == pytest.approx(composed.seconds * 30, abs=5)
    reader = imageio_ffmpeg.read_frames(str(tmp_path / "out.mp4"))
    meta = next(reader)
    reader.close()
    assert meta["size"] == (1080, 1920)
    assert meta["audio_codec"] == "aac"
    assert composed.voiceover[:3] == b"ID3" or composed.voiceover[:2] == b"\xff\xfb"
