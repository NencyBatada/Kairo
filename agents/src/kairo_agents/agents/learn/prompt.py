from functools import cache
from importlib import resources

LESSONS_PROMPT_VERSION = "learn-lessons/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.learn") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def lessons_instructions() -> str:
    return _read("lessons_v1.md")
