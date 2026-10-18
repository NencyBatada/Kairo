import asyncio
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace

import pytest
from google.genai import types

from kairo_agents.evaluation.budget import Budget, BudgetedModels, EvaluationStopped, Limits


@pytest.mark.parametrize(
    "kwargs",
    [
        {"text": 101},
        {"images": 3},
        {"videos": 2},
        {"seconds": 1201},
        {"text": 0},
        {"images": -1},
        {"videos": -1},
        {"seconds": 0},
    ],
)
def test_limits_cannot_exceed_the_approved_caps(kwargs):
    with pytest.raises(ValueError):
        Limits(**kwargs)


def test_concurrent_reservations_cannot_exceed_one_hundred_text_or_judge_requests():
    budget = Budget()

    def reserve(_):
        try:
            budget.reserve("text", "judge", "evaluation-service")
            return True
        except EvaluationStopped:
            return False

    with ThreadPoolExecutor(max_workers=16) as pool:
        accepted = list(pool.map(reserve, range(200)))
    assert sum(accepted) == 100
    assert budget.report()["requests"]["text"] == 100
    assert len(budget.report()["calls"]) == 100


def test_a_deadline_prevents_any_further_provider_request():
    now = [100.0]
    budget = Budget(Limits(seconds=1), clock=lambda: now[0])
    now[0] = 101.0
    with pytest.raises(EvaluationStopped, match="deadline"):
        budget.reserve("text", "generate_content", "model")
    assert not budget.report()["calls"]


def test_multiple_image_candidates_consume_multiple_image_slots_before_dispatch():
    calls = []

    async def generate(**kwargs):
        calls.append(kwargs)
        return SimpleNamespace(usage_metadata=None)

    async def run():
        budget = Budget()
        client = BudgetedModels(SimpleNamespace(generate_content=generate), budget)
        config = types.GenerateContentConfig(response_modalities=["IMAGE"], candidate_count=2)
        await client.generate_content(model="image", config=config)
        with pytest.raises(EvaluationStopped):
            await client.generate_content(model="image", config=config)
        assert budget.counts["images"] == 2

    asyncio.run(run())
    assert len(calls) == 1


def test_images_and_videos_are_reserved_before_calling_the_provider():
    requests = []

    async def generate(**kwargs):
        requests.append(kwargs)
        return SimpleNamespace(usage_metadata=None)

    async def run():
        budget = Budget()
        client = BudgetedModels(
            SimpleNamespace(
                generate_content=generate,
                generate_videos=generate,
            ),
            budget,
        )
        for _ in range(2):
            await client.generate_content(
                model="image",
                config=types.GenerateContentConfig(
                    response_modalities=["IMAGE"],
                ),
            )
        with pytest.raises(EvaluationStopped):
            await client.generate_content(
                model="image",
                config=types.GenerateContentConfig(
                    response_modalities=["IMAGE"],
                ),
            )
        await client.generate_videos(
            model="video",
            config=types.GenerateVideosConfig(
                number_of_videos=1,
            ),
        )
        with pytest.raises(EvaluationStopped):
            await client.generate_videos(
                model="video",
                config=types.GenerateVideosConfig(
                    number_of_videos=1,
                ),
            )
        assert budget.report()["requests"] == {"text": 0, "images": 2, "videos": 1}

    asyncio.run(run())
    assert len(requests) == 3


def test_uncertain_failures_consume_capacity_and_never_record_private_exception_text():
    async def fail(**kwargs):
        raise RuntimeError("private token")

    async def run():
        budget = Budget(Limits(text=1))
        client = BudgetedModels(SimpleNamespace(generate_content=fail), budget)
        with pytest.raises(RuntimeError):
            await client.generate_content(model="model")
        with pytest.raises(EvaluationStopped):
            await client.generate_content(model="model")
        assert budget.report()["calls"][0]["outcome"] == "failed"
        assert "private token" not in str(budget.report())

    asyncio.run(run())


def test_unknown_usage_stays_unknown_and_zero_tokens_remain_known():
    async def generate(**kwargs):
        return SimpleNamespace(usage_metadata=SimpleNamespace(prompt_token_count=0))

    async def run():
        budget = Budget()
        await BudgetedModels(SimpleNamespace(generate_content=generate), budget).generate_content(
            model="model",
        )
        call = budget.report()["calls"][0]
        assert call["inputTokens"] == 0
        assert call["outputTokens"] is None

    asyncio.run(run())
