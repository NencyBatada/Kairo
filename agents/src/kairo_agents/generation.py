from typing import Any

from google.genai import types


def generation_config(model: str, **values: Any) -> types.GenerateContentConfig:
    if model.rsplit("/", 1)[-1] == "gemini-3.8-flash":
        for name in (
            "temperature",
            "top_p",
            "top_k",
            "candidate_count",
            "frequency_penalty",
            "presence_penalty",
        ):
            values.pop(name, None)
        values.setdefault("thinking_config", types.ThinkingConfig(thinking_level="MEDIUM"))
    return types.GenerateContentConfig(**values)
