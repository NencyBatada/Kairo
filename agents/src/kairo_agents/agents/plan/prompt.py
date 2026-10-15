from functools import cache
from importlib import resources

TRIGGER_BRIEF_PROMPT_VERSION = "plan-trigger-brief/1"
BRIEF_PROMPT_VERSION = "plan-brief/1"
BRIEF_INSTRUCTION_PROMPT_VERSION = "plan-brief-instruction/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.plan") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def trigger_brief_instructions() -> str:
    return _read("trigger_brief_v1.md")


def brief_instructions() -> str:
    return _read("brief_v1.md")


def brief_instruction_instructions() -> str:
    return _read("brief_instruction_v1.md")
