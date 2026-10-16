import base64
import json
import subprocess
import wave
from io import BytesIO
from pathlib import Path
from types import SimpleNamespace

import imageio_ffmpeg
import pytest
from fastapi.testclient import TestClient
from PIL import Image

from kairo_agents.agents.create import media_api
from kairo_agents.agents.create.media_api import (
    clean_scenes,
    clean_script,
    client_source,
    media_client_source,
    speech_source,
    timeline,
)
from kairo_agents.agents.create.media_models import Scene, ScenePlan, ScriptLine
from kairo_agents.agents.create.media_prompt import (
    FOOTAGE_PROMPT_VERSION,
    IMAGE_PROMPT_VERSION,
    VIDEO_SCRIPT_PROMPT_VERSION,
    footage_instructions,
    image_instructions,
    video_script_instructions,
)
from kairo_agents.agents.create.voices import voice_for
from kairo_agents.agents.sense.prompt import DATA_START
from kairo_agents.main import app
from kairo_agents.media.ffmpeg import ffmpeg_path


def encode(data: bytes) -> str:
    return base64.b64encode(data).decode("ascii")


def picture(width: int = 800, height: int = 800, colour: str = "red") -> bytes:
    output = BytesIO()
    Image.new("RGB", (width, height), colour).save(output, "PNG")
    return output.getvalue()


def tone(seconds: float) -> bytes:
    output = BytesIO()
    with wave.open(output, "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(24000)
        writer.writeframes(b"\x10\x00" * int(24000 * seconds))
    return output.getvalue()


def clip(path: Path, colour: str) -> bytes:
    subprocess.run(
        [
            ffmpeg_path(),
            "-loglevel",
            "error",
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"color=c={colour}:s=360x640:d=1:r=24",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            str(path),
        ],
        check=True,
    )
    return path.read_bytes()


BRIEF = {
    "title": "Monsoon runners",
    "objective": "Sell waterproof shoes while monsoon searches peak.",
    "keyMessages": ["Dries in an hour"],
    "audiences": ["Commuters in Mumbai"],
}
PHOTO = {"mimeType": "image/png", "data": encode(picture())}
IMAGES_REQUEST = {
    "brief": BRIEF,
    "brandKit": {"colours": [{"hex": "#003366", "role": "primary"}]},
    "products": [
        {"id": "p1", "title": "Monsoon Runner", "description": "Waterproof", "photos": [PHOTO]},
    ],
    "shots": [
        {"id": "s1", "productId": "p1", "style": "product", "formats": ["square", "landscape"]},
        {"id": "s2", "productId": "p1", "style": "lifestyle", "formats": ["vertical"]},
    ],
}


class FakeModels:
    def __init__(self) -> None:
        self.calls: list[dict] = []
        self.images: list[bytes | None | Exception] = []
        self.text = ""
        self.error: Exception | None = None

    async def generate_content(self, **kwargs):
        self.calls.append(kwargs)
        config = kwargs["config"]
        if config.response_modalities == ["IMAGE"]:
            outcome = self.images.pop(0) if self.images else picture(1024, 1024)
            if isinstance(outcome, Exception):
                raise outcome
            parts = (
                []
                if outcome is None
                else [
                    SimpleNamespace(
                        inline_data=SimpleNamespace(data=outcome, mime_type="image/png")
                    )
                ]
            )
            return SimpleNamespace(
                candidates=[SimpleNamespace(content=SimpleNamespace(parts=parts))],
                usage_metadata=SimpleNamespace(
                    prompt_token_count=100, candidates_token_count=1290, thoughts_token_count=None
                ),
            )
        if self.error:
            raise self.error
        return SimpleNamespace(text=self.text, candidates=[], usage_metadata=None)


class FakeVideos:
    def __init__(self) -> None:
        self.calls: list[dict] = []
        self.polls = 0
        self.result: object = SimpleNamespace(
            generated_videos=[SimpleNamespace(video=SimpleNamespace(video_bytes=b"mp4-bytes"))],
            rai_media_filtered_count=0,
        )
        self.error: object = None
        self.raise_on_start: Exception | None = None
        self.never_done = False

    async def generate_videos(self, **kwargs):
        if self.raise_on_start:
            raise self.raise_on_start
        self.calls.append(kwargs)
        return SimpleNamespace(done=False, error=None, response=None)

    async def get(self, operation):
        self.polls += 1
        if self.never_done:
            return operation
        return SimpleNamespace(done=True, error=self.error, response=self.result)


class FakeSpeech:
    def __init__(self, seconds: float = 1.0) -> None:
        self.calls: list[dict] = []
        self.seconds = seconds
        self.error: Exception | None = None

    async def synthesize_speech(self, **kwargs):
        if self.error:
            raise self.error
        self.calls.append(kwargs)
        rate = kwargs["audio_config"].speaking_rate
        return SimpleNamespace(audio_content=tone(self.seconds / rate))


@pytest.fixture
def fakes(monkeypatch: pytest.MonkeyPatch):
    models = FakeModels()
    videos = FakeVideos()
    speech = FakeSpeech()
    client = SimpleNamespace(aio=SimpleNamespace(models=models))
    media = SimpleNamespace(aio=SimpleNamespace(models=videos, operations=videos))
    app.dependency_overrides[client_source] = lambda: lambda: client
    app.dependency_overrides[media_client_source] = lambda: lambda: media
    app.dependency_overrides[speech_source] = lambda: lambda: speech
    monkeypatch.setattr(media_api, "POLL_SECONDS", 0)
    yield SimpleNamespace(models=models, videos=videos, speech=speech)
    app.dependency_overrides.clear()


def post(path: str, body: dict):
    return TestClient(app).post(path, json=body)


def test_images_are_made_from_the_catalogue_photo_in_each_size(fakes) -> None:
    response = post("/create/images", IMAGES_REQUEST)

    assert response.status_code == 200
    body = response.json()
    made = {(image["shotId"], image["format"]): image for image in body["images"]}
    assert set(made) == {("s1", "square"), ("s1", "landscape"), ("s2", "vertical")}
    landscape = Image.open(BytesIO(base64.b64decode(made[("s1", "landscape")]["data"])))
    assert landscape.size == (1200, 628)
    assert made[("s1", "landscape")]["width"] == 1200
    assert made[("s1", "landscape")]["mimeType"] == "image/jpeg"
    assert made[("s2", "vertical")]["style"] == "lifestyle"
    assert body["failures"] == []
    assert body["model"] == "gemini-2.5-flash-image"
    assert body["promptVersion"] == IMAGE_PROMPT_VERSION
    assert body["usage"] == {"inputTokens": 300, "outputTokens": 3870}
    call = fakes.models.calls[0]
    parts = call["contents"][0].parts
    assert parts[0].text == image_instructions()
    assert parts[1].inline_data.mime_type == "image/png"
    assert parts[2].text.startswith(DATA_START)
    assert call["config"].response_modalities == ["IMAGE"]
    ratios = sorted(call["config"].image_config.aspect_ratio for call in fakes.models.calls)
    assert ratios == ["16:9", "1:1", "9:16"]


def test_images_report_the_sizes_that_failed(fakes) -> None:
    fakes.models.images = [RuntimeError("quota"), None, b"broken"]

    response = post("/create/images", IMAGES_REQUEST)

    assert response.status_code == 502
    assert response.json() == {"error": "model_failed"}


def test_images_keep_the_ones_that_worked(fakes) -> None:
    fakes.models.images = [picture(), None, b"broken"]

    body = post("/create/images", IMAGES_REQUEST).json()

    assert len(body["images"]) == 1
    assert sorted(failure["reason"] for failure in body["failures"]) == ["bad_image", "no_image"]


def test_image_model_comes_from_the_environment(fakes, monkeypatch) -> None:
    monkeypatch.setenv("KAIRO_CREATE_IMAGE_MODEL", "gemini-3-pro-image-preview")

    body = post("/create/images", IMAGES_REQUEST).json()

    assert body["model"] == "gemini-3-pro-image-preview"
    assert fakes.models.calls[0]["model"] == "gemini-3-pro-image-preview"


@pytest.mark.parametrize(
    "change",
    [
        {"shots": [{"id": "s1", "productId": "p9", "style": "product", "formats": ["square"]}]},
        {
            "shots": [
                {
                    "id": f"s{index}",
                    "productId": "p1",
                    "style": "product",
                    "formats": ["square", "landscape", "portrait"],
                }
                for index in range(9)
            ]
        },
        {"products": [{"id": "p1", "title": "Runner", "photos": []}]},
        {
            "products": [
                {
                    "id": "p1",
                    "title": "Runner",
                    "photos": [{"mimeType": "image/png", "data": "not base64!"}],
                }
            ]
        },
    ],
)
def test_images_refuse_bad_requests(fakes, change) -> None:
    response = post("/create/images", {**IMAGES_REQUEST, **change})

    assert response.status_code == 422


FOOTAGE_REQUEST = {
    "brief": BRIEF,
    "products": [{"id": "p1", "title": "Monsoon Runner"}],
    "frames": [
        {"productId": "p1", "mimeType": "image/png", "data": encode(picture(1080, 1920))},
        {"productId": "p1", "mimeType": "image/png", "data": encode(picture(1200, 1200))},
    ],
    "clips": 2,
    "clipSeconds": 8,
    "instruction": "Make it feel like a rainy morning",
}
SCENES = {
    "scenes": [
        {"frame": 0, "prompt": "A slow push in on the shoe on a wet street."},
        {"frame": 7, "prompt": "  An orbit around the shoe   as rain falls. "},
    ]
}


def test_footage_plans_scenes_then_animates_each_frame_with_veo(fakes) -> None:
    fakes.models.text = json.dumps(SCENES)

    response = post("/create/footage", FOOTAGE_REQUEST)

    assert response.status_code == 200
    body = response.json()
    assert [clip["index"] for clip in body["clips"]] == [0, 1]
    assert base64.b64decode(body["clips"][0]["data"]) == b"mp4-bytes"
    assert body["clips"][1]["prompt"] == "An orbit around the shoe as rain falls."
    assert body["clips"][0]["seconds"] == 8
    assert body["model"] == "veo-3.1-generate-001"
    assert body["plannerModel"] == "gemini-3.5-flash"
    assert body["promptVersion"] == FOOTAGE_PROMPT_VERSION
    planner = fakes.models.calls[0]
    assert planner["config"].system_instruction == footage_instructions()
    assert planner["config"].response_schema is ScenePlan
    assert "rainy morning" in planner["contents"]
    first = fakes.videos.calls[0]
    assert first["model"] == "veo-3.1-generate-001"
    assert first["config"].aspect_ratio == "9:16"
    assert first["config"].duration_seconds == 8
    assert first["config"].generate_audio is False
    frame = Image.open(BytesIO(first["source"].image.image_bytes))
    assert frame.size == (1080, 1920)
    assert fakes.videos.polls == 2


@pytest.mark.parametrize(
    ("setup", "reason"),
    [
        (lambda videos: setattr(videos, "error", {"code": 3}), "model_failed"),
        (
            lambda videos: setattr(
                videos,
                "result",
                SimpleNamespace(generated_videos=[], rai_media_filtered_count=1),
            ),
            "filtered",
        ),
        (
            lambda videos: setattr(
                videos, "result", SimpleNamespace(generated_videos=[], rai_media_filtered_count=0)
            ),
            "no_video",
        ),
        (lambda videos: setattr(videos, "raise_on_start", RuntimeError("quota")), "model_failed"),
    ],
)
def test_footage_reports_why_veo_failed(fakes, setup, reason) -> None:
    fakes.models.text = json.dumps(SCENES)
    setup(fakes.videos)

    response = post("/create/footage", FOOTAGE_REQUEST)

    assert response.status_code == 502
    assert response.json() == {"error": "video_failed", "reason": reason}


def test_footage_gives_up_after_the_deadline(fakes, monkeypatch) -> None:
    fakes.models.text = json.dumps(SCENES)
    fakes.videos.never_done = True
    monkeypatch.setattr(media_api, "FOOTAGE_DEADLINE_SECONDS", 0)

    response = post("/create/footage", FOOTAGE_REQUEST)

    assert response.json() == {"error": "video_failed", "reason": "timed_out"}


def test_footage_fails_when_the_planner_fails(fakes) -> None:
    fakes.models.error = RuntimeError("down")

    response = post("/create/footage", FOOTAGE_REQUEST)

    assert response.status_code == 502
    assert response.json() == {"error": "model_failed"}
    assert fakes.videos.calls == []


def test_footage_refuses_frames_that_are_not_images(fakes) -> None:
    request = {**FOOTAGE_REQUEST, "frames": [{"mimeType": "image/png", "data": encode(b"x")}]}

    assert post("/create/footage", request).status_code == 422


def test_clean_scenes_fills_missing_scenes_and_clamps_frames() -> None:
    scenes = clean_scenes(ScenePlan(scenes=[Scene(frame=5, prompt="  Push in. ")]), 3, 2)

    assert [scene.frame for scene in scenes] == [1, 1, 0]
    assert scenes[0].prompt == "Push in."
    assert scenes[1].prompt == media_api.DEFAULT_SCENE


SCRIPT = {
    "lines": [
        {"voiceover": "Rain again?", "caption": "Rain again?"},
        {"voiceover": "Monsoon Runners dry in an hour.", "caption": ""},
        {"voiceover": "  ", "caption": "dropped"},
        {"voiceover": "Shop Acme Shoes today.", "caption": "Shop Acme Shoes today"},
    ]
}


@pytest.fixture
def video_request(tmp_path: Path) -> dict:
    return {
        "brief": BRIEF,
        "businessName": "Acme Shoes",
        "language": "en",
        "clips": [
            {"data": encode(clip(tmp_path / "a.mp4", "red")), "seconds": 1},
            {"data": encode(clip(tmp_path / "b.mp4", "blue")), "seconds": 1},
        ],
        "logo": {"mimeType": "image/png", "data": encode(picture(300, 300, "white"))},
        "background": "#003366",
        "seconds": 15,
    }


def test_video_writes_the_script_reads_it_and_composes_the_video(
    fakes, video_request, tmp_path
) -> None:
    fakes.models.text = json.dumps(SCRIPT)
    fakes.speech.seconds = 0.6

    response = post("/create/video", video_request)

    assert response.status_code == 200
    body = response.json()
    assert body["width"] == 1080 and body["height"] == 1920
    assert body["language"] == "en"
    assert body["voice"] == "en-IN-Chirp3-HD-Achird"
    assert body["promptVersion"] == VIDEO_SCRIPT_PROMPT_VERSION
    assert [line["caption"] for line in body["script"]] == [
        "Rain again?",
        "Monsoon Runners dry in an hour.",
        "Shop Acme Shoes today",
    ]
    assert body["script"][0]["start"] == 0.3
    assert body["video"]["mimeType"] == "video/mp4"
    assert body["voiceover"]["mimeType"] == "audio/mpeg"
    video = tmp_path / "video.mp4"
    video.write_bytes(base64.b64decode(body["video"]["data"]))
    _, seconds = imageio_ffmpeg.count_frames_and_secs(str(video))
    assert seconds == pytest.approx(4.5, abs=0.1)
    call = fakes.models.calls[0]
    assert call["config"].system_instruction == video_script_instructions()
    spoken = fakes.speech.calls[0]
    assert spoken["voice"].language_code == "en-IN"
    assert spoken["audio_config"].speaking_rate == 1.0


def test_video_speeds_up_or_drops_lines_that_do_not_fit(fakes, video_request) -> None:
    fakes.models.text = json.dumps(SCRIPT)
    fakes.speech.seconds = 2.0

    body = post("/create/video", video_request).json()

    rates = {call["audio_config"].speaking_rate for call in fakes.speech.calls}
    assert max(rates) == media_api.MAX_RATE
    assert len(body["script"]) < 3
    assert body["script"][-1]["end"] <= 4.5


def test_video_reuses_a_given_script_without_the_model(fakes, video_request) -> None:
    video_request["script"] = [{"voiceover": "Stay dry.", "caption": "Stay dry"}]

    body = post("/create/video", video_request).json()

    assert fakes.models.calls == []
    assert [line["voiceover"] for line in body["script"]] == ["Stay dry."]
    assert body["usage"] is None


def test_video_refuses_a_language_without_a_voice(fakes, video_request) -> None:
    video_request["language"] = "ta"

    response = post("/create/video", video_request)

    assert response.status_code == 422
    assert response.json() == {"error": "no_voice"}


def test_video_reports_speech_failures(fakes, video_request) -> None:
    fakes.models.text = json.dumps(SCRIPT)
    fakes.speech.error = RuntimeError("tts down")

    response = post("/create/video", video_request)

    assert response.status_code == 502
    assert response.json() == {"error": "speech_failed"}


def test_video_reports_compose_failures(fakes, video_request) -> None:
    video_request["script"] = [{"voiceover": "Stay dry.", "caption": "Stay dry"}]
    video_request["clips"] = [{"data": encode(b"not a video"), "seconds": 1}]

    response = post("/create/video", video_request)

    assert response.status_code == 502
    assert response.json() == {"error": "compose_failed"}


def test_video_reports_a_script_the_model_could_not_write(fakes, video_request) -> None:
    fakes.models.text = json.dumps({"lines": [{"voiceover": " ", "caption": ""}]})

    response = post("/create/video", video_request)

    assert response.status_code == 502


def test_clean_script_trims_captions_at_a_word() -> None:
    lines = clean_script([ScriptLine(voiceover="Go", caption="word " * 20)] * 7)

    assert len(lines) == media_api.MAX_LINES
    assert len(lines[0].caption) <= media_api.MAX_CAPTION
    assert not lines[0].caption.endswith(" ")


def test_timeline_spaces_lines_and_stops_at_the_end() -> None:
    timed = timeline([ScriptLine(voiceover="a", caption="a")] * 2, [1.0, 9.0], 5.0)

    assert [(line.start, line.end) for line in timed] == [(0.3, 1.3), (1.55, 5.0)]


def test_voices_can_be_overridden_per_language(monkeypatch) -> None:
    monkeypatch.setenv("KAIRO_TTS_VOICE_EN", "en-IN-Chirp3-HD-Kore")

    assert voice_for("en").name == "en-IN-Chirp3-HD-Kore"
    assert voice_for("xx") is None
