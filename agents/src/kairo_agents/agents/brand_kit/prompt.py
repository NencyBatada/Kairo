import json
from functools import cache
from importlib import resources

from kairo_agents.agents.brand_kit.models import DraftRequest

PROMPT_VERSION = "brand-kit-draft/1"
DATA_START = "<<<KAIRO_DATA"
DATA_END = "KAIRO_DATA>>>"


@cache
def instructions() -> str:
    prompt = resources.files("kairo_agents.agents.brand_kit") / "prompts" / "draft_v1.md"
    return prompt.read_text(encoding="utf-8")


def data_block(request: DraftRequest) -> str:
    payload = json.dumps(request.model_dump(by_alias=True), ensure_ascii=False)
    payload = payload.replace("<", "\\u003c").replace(">", "\\u003e")
    return f"{DATA_START}\n{payload}\n{DATA_END}"
