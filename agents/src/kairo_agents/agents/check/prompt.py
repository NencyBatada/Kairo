from functools import cache
from importlib import resources

COPY_PROMPT_VERSION = "check-copy/1"
IMAGE_PROMPT_VERSION = "check-image/1"
VIDEO_PROMPT_VERSION = "check-video/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.check") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def copy_instructions() -> str:
    return _read("check_copy_v1.md")


def image_instructions() -> str:
    return _read("check_image_v1.md")


def video_instructions() -> str:
    return _read("check_video_v1.md")
