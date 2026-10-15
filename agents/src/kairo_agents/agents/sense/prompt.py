import json
from functools import cache
from importlib import resources

from pydantic import BaseModel

MATCH_PROMPT_VERSION = "sense-match/1"
EXPLAIN_PROMPT_VERSION = "sense-explain/1"
DATA_START = "<<<KAIRO_DATA"
DATA_END = "KAIRO_DATA>>>"
NO_SOURCE = "NO_SOURCE"


@cache
def _read(name: str) -> str:
    prompt = resources.files("kairo_agents.agents.sense") / "prompts" / name
    return prompt.read_text(encoding="utf-8")


def match_instructions() -> str:
    return _read("match_v1.md")


def explain_instructions() -> str:
    return _read("explain_v1.md")


def data_block(data: BaseModel | dict) -> str:
    content = data.model_dump(by_alias=True) if isinstance(data, BaseModel) else data
    payload = json.dumps(content, ensure_ascii=False)
    payload = payload.replace("<", "\\u003c").replace(">", "\\u003e")
    return f"{DATA_START}\n{payload}\n{DATA_END}"
