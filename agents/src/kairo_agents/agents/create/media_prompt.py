from functools import cache
from importlib import resources

IMAGE_PROMPT_VERSION = "create-image/1"
FOOTAGE_PROMPT_VERSION = "create-footage/1"
VIDEO_SCRIPT_PROMPT_VERSION = "create-video-script/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.create") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def image_instructions() -> str:
    return _read("image_v1.md")


def footage_instructions() -> str:
    return _read("footage_v1.md")


def video_script_instructions() -> str:
    return _read("video_script_v1.md")
