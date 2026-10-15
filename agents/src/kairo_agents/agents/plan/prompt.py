from functools import cache
from importlib import resources

TRIGGER_BRIEF_PROMPT_VERSION = "plan-trigger-brief/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.plan") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def trigger_brief_instructions() -> str:
    return _read("trigger_brief_v1.md")
