import pytest
from google.genai import types

from kairo_agents.generation import generation_config


@pytest.mark.parametrize("model", ["gemini-3.8-flash", "publishers/google/models/gemini-3.8-flash"])
def test_gemini_38_uses_thinking_and_omits_unsupported_sampling_fields(model):
    config = generation_config(
        model,
        temperature=0.2,
        top_p=0.9,
        top_k=40,
        candidate_count=1,
        frequency_penalty=0.1,
        presence_penalty=0.1,
        system_instruction="test",
        response_mime_type="application/json",
    )
    data = config.model_dump(exclude_none=True)
    for field in (
        "temperature",
        "top_p",
        "top_k",
        "candidate_count",
        "frequency_penalty",
        "presence_penalty",
    ):
        assert field not in data
    assert config.thinking_config.thinking_level == types.ThinkingLevel.MEDIUM
    assert config.system_instruction == "test"
    assert config.response_mime_type == "application/json"


def test_other_models_keep_their_existing_generation_settings():
    config = generation_config("gemini-3.5-flash", temperature=0.3)
    assert config.temperature == 0.3
    assert config.thinking_config is None
