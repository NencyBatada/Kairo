import asyncio
import logging
import os
from collections.abc import Callable
from functools import cache
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google import genai
from google.cloud import texttospeech
from google.genai import types

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.create.media_models import (
    FootageClip,
    FootageRequest,
    FootageResponse,
    GeneratedImage,
    ImageFailure,
    ImagesRequest,
    ImagesResponse,
    MediaOut,
    ReferenceProduct,
    Scene,
    ScenePlan,
    ScriptLine,
    Shot,
    TimedLine,
    VideoRequest,
    VideoResponse,
    VoiceoverScript,
)
from kairo_agents.agents.create.media_prompt import (
    FOOTAGE_PROMPT_VERSION,
    IMAGE_PROMPT_VERSION,
    VIDEO_SCRIPT_PROMPT_VERSION,
    footage_instructions,
    image_instructions,
    video_script_instructions,
)
from kairo_agents.agents.create.voices import Voice, voice_for
from kairo_agents.agents.plan.api import usage_of
from kairo_agents.agents.plan.models import Usage
from kairo_agents.agents.sense.prompt import data_block
from kairo_agents.media.audio import SAMPLE_RATE, BadAudio, Pcm, place, read_wav
from kairo_agents.media.captions import HEIGHT as VIDEO_HEIGHT
from kairo_agents.media.captions import WIDTH, Caption
from kairo_agents.media.compose import END_CARD_SECONDS, body_seconds, compose
from kairo_agents.media.ffmpeg import FfmpegError
from kairo_agents.media.formats import FORMATS, BadImage, fit

DEFAULT_MODEL = "gemini-3.5-flash"
DEFAULT_IMAGE_MODEL = "gemini-2.5-flash-image"
DEFAULT_VIDEO_MODEL = "veo-3.1-generate-001"
DEFAULT_MEDIA_LOCATION = "us-central1"
IMAGES_AT_ONCE = 4
POLL_SECONDS = 10.0
FOOTAGE_DEADLINE_SECONDS = 330.0
MAX_SCENE_PROMPT = 1000
MAX_CAPTION = 60
MAX_LINES = 5
LINE_GAP = 0.25
LEAD = 0.3
MAX_RATE = 1.25
NEGATIVE_PROMPT = (
    "text, captions, subtitles, watermark, logo overlay, price tags, distorted product, "
    "morphing, extra limbs"
)
DEFAULT_SCENE = "A slow push in on the product in soft natural light, the product stays unchanged."

logger = logging.getLogger(__name__)
router = APIRouter()


class FootageFailed(Exception):
    def __init__(self, reason: str) -> None:
        super().__init__(reason)
        self.reason = reason


def model_name() -> str:
    return os.environ.get("KAIRO_CREATE_MODEL") or DEFAULT_MODEL


def image_model_name() -> str:
    return os.environ.get("KAIRO_CREATE_IMAGE_MODEL") or DEFAULT_IMAGE_MODEL


def video_model_name() -> str:
    return os.environ.get("KAIRO_CREATE_VIDEO_MODEL") or DEFAULT_VIDEO_MODEL


@cache
def default_media_client() -> genai.Client:
    return genai.Client(
        vertexai=True,
        project=os.environ.get("GOOGLE_CLOUD_PROJECT"),
        location=os.environ.get("KAIRO_MEDIA_LOCATION") or DEFAULT_MEDIA_LOCATION,
    )


@cache
def default_speech_client() -> texttospeech.TextToSpeechAsyncClient:
    return texttospeech.TextToSpeechAsyncClient()


def client_source() -> Callable[[], Any]:
    return default_client


def media_client_source() -> Callable[[], Any]:
    return default_media_client


def speech_source() -> Callable[[], Any]:
    return default_speech_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]
MediaClientSource = Annotated[Callable[[], Any], Depends(media_client_source)]
SpeechSource = Annotated[Callable[[], Any], Depends(speech_source)]


@router.post("/create/images", response_model=ImagesResponse)
async def images(request: ImagesRequest, clients: ClientSource) -> Any:
    model = image_model_name()
    products = {product.id: product for product in request.products}
    gate = asyncio.Semaphore(IMAGES_AT_ONCE)
    jobs = [
        (shot, format_name) for shot in request.shots for format_name in dict.fromkeys(shot.formats)
    ]

    async def one(shot: Shot, format_name: str) -> tuple[GeneratedImage | ImageFailure, Any]:
        async with gate:
            return await generate_image(
                clients(), model, request, products[shot.product_id], shot, format_name
            )

    outcomes = await asyncio.gather(*(one(shot, name) for shot, name in jobs))
    results = [result for result, _ in outcomes]
    made = [result for result in results if isinstance(result, GeneratedImage)]
    failures = [result for result in results if isinstance(result, ImageFailure)]
    if not made:
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return ImagesResponse(
        images=made,
        failures=failures,
        model=model,
        prompt_version=IMAGE_PROMPT_VERSION,
        usage=total_usage([response for _, response in outcomes]),
    )


def total_usage(responses: list[Any]) -> Usage | None:
    counted = [usage for response in responses if (usage := usage_of(response)) is not None]
    if not counted:
        return None
    inputs = [usage.input_tokens for usage in counted if usage.input_tokens is not None]
    outputs = [usage.output_tokens for usage in counted if usage.output_tokens is not None]
    return Usage(
        input_tokens=sum(inputs) if inputs else None,
        output_tokens=sum(outputs) if outputs else None,
    )


async def generate_image(
    client: Any,
    model: str,
    request: ImagesRequest,
    product: ReferenceProduct,
    shot: Shot,
    format_name: str,
) -> tuple[GeneratedImage | ImageFailure, Any]:
    target = FORMATS[format_name]
    block = data_block(
        {
            "brief": request.brief.model_dump(by_alias=True),
            "product": {"title": product.title, "description": product.description},
            "style": shot.style,
            "format": {"name": target.name, "aspectRatio": target.aspect},
            "instruction": request.instruction,
            "brandKit": request.brand_kit,
        }
    )
    parts = [
        types.Part.from_text(text=image_instructions()),
        *[
            types.Part.from_bytes(data=photo.data, mime_type=photo.mime_type)
            for photo in product.photos
        ],
        types.Part.from_text(text=block),
    ]
    try:
        response = await client.aio.models.generate_content(
            model=model,
            contents=[types.Content(role="user", parts=parts)],
            config=types.GenerateContentConfig(
                response_modalities=["IMAGE"],
                image_config=types.ImageConfig(aspect_ratio=target.aspect),
            ),
        )
    except Exception:
        logger.exception("Create couldn't make image %s (%s).", shot.id, format_name)
        return ImageFailure(shot_id=shot.id, format=target.name, reason="model_failed"), None
    data = first_image(response)
    if data is None:
        return ImageFailure(shot_id=shot.id, format=target.name, reason="no_image"), response
    try:
        fitted = await asyncio.to_thread(fit, data, target)
    except BadImage:
        return ImageFailure(shot_id=shot.id, format=target.name, reason="bad_image"), response
    image = GeneratedImage(
        shot_id=shot.id,
        product_id=product.id,
        style=shot.style,
        format=target.name,
        width=target.width,
        height=target.height,
        data=fitted,
        prompt=block,
    )
    return image, response


def first_image(response: Any) -> bytes | None:
    for candidate in getattr(response, "candidates", None) or []:
        content = getattr(candidate, "content", None)
        for part in getattr(content, "parts", None) or []:
            inline = getattr(part, "inline_data", None)
            data = getattr(inline, "data", None)
            if isinstance(data, bytes) and data:
                return data
    return None


@router.post("/create/footage", response_model=FootageResponse)
async def footage(
    request: FootageRequest, clients: ClientSource, media_clients: MediaClientSource
) -> Any:
    planner = model_name()
    try:
        frames = [
            await asyncio.to_thread(fit, frame.data, FORMATS["vertical"])
            for frame in request.frames
        ]
    except BadImage:
        return JSONResponse(status_code=422, content={"error": "bad_frame"})
    block = data_block(
        {
            "brief": request.brief.model_dump(by_alias=True),
            "products": [product.model_dump(by_alias=True) for product in request.products],
            "frames": [
                {"number": index, "productId": frame.product_id}
                for index, frame in enumerate(request.frames)
            ],
            "scenes": request.clips,
            "secondsPerScene": request.clip_seconds,
            "instruction": request.instruction,
            "brandKit": request.brand_kit,
        }
    )
    try:
        response = await clients().aio.models.generate_content(
            model=planner,
            contents=block,
            config=types.GenerateContentConfig(
                system_instruction=footage_instructions(),
                response_mime_type="application/json",
                response_schema=ScenePlan,
                temperature=0.4,
            ),
        )
        plan = ScenePlan.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Create couldn't plan the video scenes.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    scenes = clean_scenes(plan, request.clips, len(frames))
    video_model = video_model_name()
    try:
        clips = await asyncio.gather(
            *(
                make_clip(media_clients(), video_model, scene, frames[scene.frame], request)
                for scene in scenes
            )
        )
    except FootageFailed as failure:
        return JSONResponse(
            status_code=502, content={"error": "video_failed", "reason": failure.reason}
        )
    return FootageResponse(
        clips=[
            FootageClip(index=index, prompt=scene.prompt, data=data, seconds=request.clip_seconds)
            for index, (scene, data) in enumerate(zip(scenes, clips, strict=True))
        ],
        model=video_model,
        planner_model=planner,
        prompt_version=FOOTAGE_PROMPT_VERSION,
        usage=usage_of(response),
    )


def clean_scenes(plan: ScenePlan, count: int, frames: int) -> list[Scene]:
    scenes = [
        Scene(frame=min(max(scene.frame, 0), frames - 1), prompt=prompt)
        for scene in plan.scenes
        if (prompt := " ".join(scene.prompt.split())[:MAX_SCENE_PROMPT])
    ][:count]
    while len(scenes) < count:
        scenes.append(Scene(frame=len(scenes) % frames, prompt=DEFAULT_SCENE))
    return scenes


async def make_clip(
    client: Any, model: str, scene: Scene, frame: bytes, request: FootageRequest
) -> bytes:
    try:
        operation = await client.aio.models.generate_videos(
            model=model,
            source=types.GenerateVideosSource(
                prompt=scene.prompt,
                image=types.Image(image_bytes=frame, mime_type="image/jpeg"),
            ),
            config=types.GenerateVideosConfig(
                aspect_ratio="9:16",
                duration_seconds=request.clip_seconds,
                number_of_videos=1,
                generate_audio=False,
                resolution="1080p",
                negative_prompt=NEGATIVE_PROMPT,
            ),
        )
        loop = asyncio.get_running_loop()
        deadline = loop.time() + FOOTAGE_DEADLINE_SECONDS
        while not operation.done:
            if loop.time() > deadline:
                raise FootageFailed("timed_out")
            await asyncio.sleep(POLL_SECONDS)
            operation = await client.aio.operations.get(operation)
    except FootageFailed:
        raise
    except Exception as error:
        logger.exception("Veo couldn't make a clip.")
        raise FootageFailed("model_failed") from error
    if operation.error:
        logger.error("Veo returned an error: %s", operation.error)
        raise FootageFailed("model_failed")
    result = operation.response
    videos = getattr(result, "generated_videos", None) or []
    data = getattr(getattr(videos[0], "video", None), "video_bytes", None) if videos else None
    if not data:
        filtered = getattr(result, "rai_media_filtered_count", None) or 0
        raise FootageFailed("filtered" if filtered else "no_video")
    return data


@router.post("/create/video", response_model=VideoResponse)
async def video(request: VideoRequest, clients: ClientSource, speech: SpeechSource) -> Any:
    voice = voice_for(request.language)
    if voice is None:
        return JSONResponse(status_code=422, content={"error": "no_voice"})
    seconds = body_seconds([clip.seconds for clip in request.clips], request.seconds)
    total = round(seconds + END_CARD_SECONDS, 2)
    model = model_name()
    usage: Usage | None = None
    if request.script:
        lines = clean_script(request.script)
    else:
        try:
            response = await clients().aio.models.generate_content(
                model=model,
                contents=data_block(
                    {
                        "brief": request.brief.model_dump(by_alias=True),
                        "businessName": request.business_name,
                        "language": request.language,
                        "seconds": total,
                        "instruction": request.instruction,
                        "brandKit": request.brand_kit,
                    }
                ),
                config=types.GenerateContentConfig(
                    system_instruction=video_script_instructions(),
                    response_mime_type="application/json",
                    response_schema=VoiceoverScript,
                    temperature=0.4,
                ),
            )
            lines = clean_script(VoiceoverScript.model_validate_json(response.text or "").lines)
            usage = usage_of(response)
        except Exception:
            logger.exception("Create couldn't write the video script.")
            return JSONResponse(status_code=502, content={"error": "model_failed"})
    if not lines:
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    try:
        lines, spoken = await narrate(speech(), voice, lines, total)
    except Exception:
        logger.exception("Text-to-Speech couldn't read the voiceover.")
        return JSONResponse(status_code=502, content={"error": "speech_failed"})
    timed = timeline(lines, [pcm.seconds for pcm in spoken], total)
    captions = [Caption(line.start, line.end, line.caption) for line in timed]
    if request.business_name and request.logo is None:
        captions.append(Caption(seconds + 0.2, total, request.business_name))
    try:
        placed = [(line.start, pcm) for line, pcm in zip(timed, spoken, strict=True)]
        voiceover = place(placed, total)
        composed = await compose(
            [(clip.data, clip.seconds) for clip in request.clips],
            voiceover,
            captions,
            voice.script,
            request.seconds,
            request.logo.data if request.logo else None,
            request.background,
        )
    except (BadAudio, FfmpegError):
        logger.exception("Create couldn't put the video together.")
        return JSONResponse(status_code=502, content={"error": "compose_failed"})
    return VideoResponse(
        video=MediaOut(mime_type="video/mp4", data=composed.video, seconds=composed.seconds),
        voiceover=MediaOut(mime_type="audio/mpeg", data=composed.voiceover, seconds=total),
        width=WIDTH,
        height=VIDEO_HEIGHT,
        language=request.language,
        voice=voice.name,
        script=timed,
        model=model,
        prompt_version=VIDEO_SCRIPT_PROMPT_VERSION,
        usage=usage,
    )


def clean_script(lines: list[ScriptLine]) -> list[ScriptLine]:
    cleaned = []
    for line in lines:
        spoken = " ".join(line.voiceover.split())
        if not spoken:
            continue
        caption = shorten(" ".join(line.caption.split()) or spoken, MAX_CAPTION)
        cleaned.append(ScriptLine(voiceover=spoken, caption=caption))
    return cleaned[:MAX_LINES]


def shorten(text: str, limit: int) -> str:
    if len(text) <= limit:
        return text
    cut = text[:limit].rsplit(" ", 1)[0]
    return cut or text[:limit]


async def narrate(
    client: Any, voice: Voice, lines: list[ScriptLine], total: float
) -> tuple[list[ScriptLine], list[Pcm]]:
    budget = total - 2 * LEAD
    spoken = await speak_all(client, voice, lines, 1.0)
    length = spoken_length(spoken)
    if length > budget:
        rate = min(MAX_RATE, length / budget * 1.02)
        spoken = await speak_all(client, voice, lines, rate)
    while len(lines) > 1 and spoken_length(spoken) > budget:
        lines, spoken = lines[:-1], spoken[:-1]
    return lines, spoken


async def speak_all(client: Any, voice: Voice, lines: list[ScriptLine], rate: float) -> list[Pcm]:
    said = await asyncio.gather(*(speak(client, voice, line.voiceover, rate) for line in lines))
    return list(said)


async def speak(client: Any, voice: Voice, text: str, rate: float) -> Pcm:
    response = await client.synthesize_speech(
        input=texttospeech.SynthesisInput(text=text),
        voice=texttospeech.VoiceSelectionParams(language_code=voice.language_code, name=voice.name),
        audio_config=texttospeech.AudioConfig(
            audio_encoding=texttospeech.AudioEncoding.LINEAR16,
            sample_rate_hertz=SAMPLE_RATE,
            speaking_rate=rate,
        ),
    )
    return read_wav(response.audio_content)


def spoken_length(spoken: list[Pcm]) -> float:
    return sum(pcm.seconds for pcm in spoken) + LINE_GAP * max(0, len(spoken) - 1)


def timeline(lines: list[ScriptLine], lengths: list[float], total: float) -> list[TimedLine]:
    timed = []
    start = LEAD
    for line, length in zip(lines, lengths, strict=True):
        end = min(total, start + length)
        timed.append(
            TimedLine(
                voiceover=line.voiceover,
                caption=line.caption,
                start=round(start, 2),
                end=round(end, 2),
            )
        )
        start = end + LINE_GAP
    return timed
