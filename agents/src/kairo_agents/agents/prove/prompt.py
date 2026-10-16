from functools import cache
from importlib import resources

REPORT_PROMPT_VERSION = "prove-report/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.prove") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def report_instructions() -> str:
    return _read("report_v1.md")
