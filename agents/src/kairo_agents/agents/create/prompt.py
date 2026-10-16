from functools import cache
from importlib import resources

COPY_PROMPT_VERSION = "create-copy/1"
COPY_INSTRUCTION_PROMPT_VERSION = "create-copy-instruction/1"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.create") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def search_instructions() -> str:
    return _read("search_v1.md")


def demand_gen_instructions() -> str:
    return _read("demand_gen_v1.md")


def titles_instructions() -> str:
    return _read("titles_v1.md")


def copy_instruction_instructions() -> str:
    return _read("copy_instruction_v1.md")
