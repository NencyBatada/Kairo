import asyncio
import threading
import time
from collections.abc import Callable
from dataclasses import asdict, dataclass
from typing import Any


class EvaluationStopped(RuntimeError):
    pass


async def retry_text(budget: "Budget", operation: Callable[..., Any]) -> Any:
    for attempt in range(3):
        try:
            return await operation()
        except Exception as error:
            code = getattr(error, "code", None)
            if code is None:
                code = getattr(getattr(error, "response", None), "status_code", None)
            if code not in (429, 503) or attempt == 2:
                raise
            if budget.counts["text"] >= budget.limits.text:
                raise EvaluationStopped("evaluation_budget_exceeded") from None
            delay = 5 * (attempt + 1)
            if budget.remaining() <= delay:
                raise EvaluationStopped("evaluation_deadline_exceeded") from None
            await asyncio.sleep(delay)
    raise EvaluationStopped("incomplete_retry")


@dataclass(frozen=True)
class Limits:
    text: int = 100
    images: int = 2
    videos: int = 1
    seconds: float = 900

    def __post_init__(self) -> None:
        if not (0 < self.text <= 100 and 0 <= self.images <= 2 and 0 <= self.videos <= 1):
            raise ValueError("Evaluation limits exceed the approved request caps")
        if not 0 < self.seconds <= 1200:
            raise ValueError("An evaluation must finish within twenty minutes")


class Budget:
    def __init__(self, limits: Limits | None = None, clock: Callable[[], float] = time.monotonic):
        self.limits = limits or Limits()
        self.clock = clock
        self.started = clock()
        self.lock = threading.Lock()
        self.calls: list[dict[str, Any]] = []
        self.counts = {"text": 0, "images": 0, "videos": 0}
        self.fatal_error: str | None = None

    def remaining(self) -> float:
        return max(0, self.limits.seconds - (self.clock() - self.started))

    def reserve(self, kind: str, operation: str, model: str, count: int = 1) -> dict[str, Any]:
        with self.lock:
            if count < 1 or kind not in self.counts:
                raise ValueError("Unknown evaluation request")
            if self.fatal_error is not None:
                raise EvaluationStopped(self.fatal_error)
            if not self.remaining():
                raise EvaluationStopped("evaluation_deadline_exceeded")
            if self.counts[kind] + count > getattr(self.limits, kind):
                raise EvaluationStopped("evaluation_budget_exceeded")
            self.counts[kind] += count
            call = {
                "kind": kind,
                "operation": operation,
                "model": model,
                "resolvedModelVersion": None,
                "units": count,
                "outcome": "incomplete",
                "inputTokens": None,
                "outputTokens": None,
                "thoughtTokens": None,
                "cachedInputTokens": None,
                "totalTokens": None,
            }
            self.calls.append(call)
            return call

    def report(self) -> dict[str, Any]:
        with self.lock:
            return {
                "limits": asdict(self.limits),
                "requests": self.counts.copy(),
                "elapsedSeconds": round(self.clock() - self.started, 3),
                "fatalError": self.fatal_error,
                "calls": [item.copy() for item in self.calls],
            }


class BudgetedModels:
    def __init__(self, models: Any, budget: Budget, media_models: Any = None):
        self.models = models
        self.media_models = media_models if media_models is not None else models
        self.budget = budget

    async def generate_content(self, **kwargs: Any) -> Any:
        config = kwargs.get("config")
        modalities = getattr(config, "response_modalities", None) or []
        kind = "images" if "IMAGE" in modalities else "text"
        count = (getattr(config, "candidate_count", None) or 1) if kind == "images" else 1
        return await self.invoke("generate_content", kind, count, kwargs)

    async def generate_videos(self, **kwargs: Any) -> Any:
        count = getattr(kwargs.get("config"), "number_of_videos", None) or 1
        return await self.invoke("generate_videos", "videos", count, kwargs)

    async def invoke(self, operation: str, kind: str, count: int, kwargs: Any) -> Any:
        if kind == "text":
            return await retry_text(
                self.budget, lambda: self.attempt(operation, kind, count, kwargs)
            )
        return await self.attempt(operation, kind, count, kwargs)

    async def attempt(self, operation: str, kind: str, count: int, kwargs: Any) -> Any:
        call = self.budget.reserve(kind, operation, str(kwargs.get("model", "unknown")), count)
        started = self.budget.clock()
        try:
            models = self.media_models if operation == "generate_videos" else self.models
            result = await getattr(models, operation)(**kwargs)
            call["resolvedModelVersion"] = getattr(result, "model_version", None)
            usage = getattr(result, "usage_metadata", None)
            if usage is not None:
                call["inputTokens"] = getattr(usage, "prompt_token_count", None)
                call["outputTokens"] = getattr(usage, "candidates_token_count", None)
                call["thoughtTokens"] = getattr(usage, "thoughts_token_count", None)
                call["cachedInputTokens"] = getattr(usage, "cached_content_token_count", None)
                call["totalTokens"] = getattr(usage, "total_token_count", None)
            call["outcome"] = "succeeded"
            return result
        except BaseException as error:
            call["outcome"] = "failed"
            call["errorType"] = type(error).__name__
            code = getattr(error, "code", None)
            if isinstance(code, int):
                call["statusCode"] = code
                if code in (403, 404):
                    self.budget.fatal_error = "required_provider_unavailable"
            raise
        finally:
            call["seconds"] = round(self.budget.clock() - started, 3)
