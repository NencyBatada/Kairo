import asyncio
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace

import pytest
from google.genai import errors, types

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


@pytest.mark.parametrize("status", [403, 404])
def test_unavailable_required_models_stop_further_spending_and_remain_failing(status):
    calls = []

    async def unavailable(**kwargs):
        calls.append(kwargs)
        raise errors.ClientError(status, {"error": {"message": "private provider text"}})

    async def run():
        budget = Budget()
        client = BudgetedModels(SimpleNamespace(generate_content=unavailable), budget)
        with pytest.raises(errors.ClientError):
            await client.generate_content(model="required-model")
        with pytest.raises(EvaluationStopped, match="required_provider_unavailable"):
            await client.generate_content(
                model="image", config=types.GenerateContentConfig(response_modalities=["IMAGE"])
            )
        assert budget.counts == {"text": 1, "images": 0, "videos": 0}
        assert budget.calls[0]["statusCode"] == status
        assert "private provider text" not in str(budget.report())

    asyncio.run(run())
    assert len(calls) == 1


@pytest.mark.parametrize("status", [429, 503])
def test_temporary_text_failures_retry_with_a_new_reservation_and_preserved_failure(
    monkeypatch, status
):
    calls = []
    delays = []

    async def sleep(delay):
        delays.append(delay)

    async def generate(**kwargs):
        calls.append(kwargs)
        if len(calls) < 2:
            raise errors.ClientError(status, {"error": {"message": "temporary"}})
        return SimpleNamespace(usage_metadata=None)

    monkeypatch.setattr("kairo_agents.evaluation.budget.asyncio.sleep", sleep)
    budget = Budget()
    model = BudgetedModels(SimpleNamespace(generate_content=generate), budget)
    asyncio.run(model.generate_content(model="text"))
    assert len(calls) == 2
    assert delays == [5]
    assert budget.counts["text"] == 2
    assert [call["outcome"] for call in budget.calls] == ["failed", "succeeded"]


@pytest.mark.parametrize("limit,seconds,expected", [(1, 900, "budget"), (100, 1, "deadline")])
def test_temporary_failures_never_retry_past_a_request_or_time_limit(
    monkeypatch, limit, seconds, expected
):
    async def generate(**kwargs):
        raise errors.ClientError(429, {"error": {"message": "temporary"}})

    async def sleep(delay):
        raise AssertionError("No retry is allowed")

    monkeypatch.setattr("kairo_agents.evaluation.budget.asyncio.sleep", sleep)
    budget = Budget(Limits(text=limit, seconds=seconds))
    model = BudgetedModels(SimpleNamespace(generate_content=generate), budget)
    with pytest.raises(EvaluationStopped, match=expected):
        asyncio.run(model.generate_content(model="text"))
    assert budget.counts["text"] == 1


def test_persistent_temporary_errors_stop_after_three_counted_attempts(monkeypatch):
    async def generate(**kwargs):
        raise errors.ClientError(503, {"error": {"message": "temporary"}})

    async def sleep(delay):
        pass

    monkeypatch.setattr("kairo_agents.evaluation.budget.asyncio.sleep", sleep)
    budget = Budget()
    model = BudgetedModels(SimpleNamespace(generate_content=generate), budget)
    with pytest.raises(errors.ClientError):
        asyncio.run(model.generate_content(model="text"))
    assert budget.counts["text"] == 3


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
