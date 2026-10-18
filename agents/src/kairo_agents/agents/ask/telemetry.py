import time
from typing import Any

from opentelemetry import metrics, trace
from opentelemetry.trace import StatusCode


class AskModelTelemetry:
    def __init__(self, model: str):
        self.model = model
        self.span: Any = None
        self.started = 0.0

    def before(self, callback_context: Any, llm_request: Any) -> None:
        self.finish("failed")
        self.started = time.monotonic()
        self.span = trace.get_tracer("Kairo").start_span("model.ask")
        self.span.set_attribute("gen_ai.request.model", self.model)

    def after(self, callback_context: Any, llm_response: Any) -> None:
        self.finish("succeeded")

    def error(self, callback_context: Any, llm_request: Any, error: Exception) -> None:
        self.finish("failed")

    def finish(self, outcome: str) -> None:
        if self.span is None:
            return
        if outcome == "failed":
            self.span.set_status(StatusCode.ERROR)
        self.span.end()
        metrics.get_meter("Kairo").create_histogram("kairo.model.duration", "s").record(
            time.monotonic() - self.started,
            {"model": self.model, "operation": "ask", "outcome": outcome},
        )
        self.span = None
