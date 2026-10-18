import json
import logging
import os
import time
from collections.abc import Callable
from contextvars import ContextVar
from typing import Any
from uuid import UUID

from opentelemetry import metrics, trace
from opentelemetry.propagate import inject
from opentelemetry.trace import SpanKind, StatusCode
from opentelemetry.trace.propagation.tracecontext import TraceContextTextMapPropagator

correlation: ContextVar[dict[str, str] | None] = ContextVar("kairo_correlation", default=None)
logger = logging.getLogger(__name__)


class CloudFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        fields: dict[str, Any] = {
            "severity": "WARNING" if record.levelno == logging.WARNING else record.levelname,
            "message": record.msg if record.name.startswith("kairo_agents.") else "Application log",
            "category": record.name,
            **(correlation.get() or {}),
        }
        if record.exc_info and record.exc_info[0]:
            fields["exceptionType"] = record.exc_info[0].__name__
        span = trace.get_current_span().get_span_context()
        if span.is_valid:
            fields["traceId"] = format(span.trace_id, "032x")
            fields["spanId"] = format(span.span_id, "016x")
            if project := os.environ.get("GOOGLE_CLOUD_PROJECT"):
                fields["logging.googleapis.com/trace"] = (
                    f"projects/{project}/traces/{span.trace_id:032x}"
                )
                fields["logging.googleapis.com/spanId"] = format(span.span_id, "016x")
        return json.dumps(fields, ensure_ascii=False, default=lambda _: "[redacted]")


def configure() -> None:
    if os.environ.get("K_SERVICE"):
        handler = logging.StreamHandler()
        handler.setFormatter(CloudFormatter())
        logging.getLogger().handlers = [handler]
        logging.getLogger().setLevel(logging.INFO)
        for name in ("uvicorn", "uvicorn.error", "uvicorn.access"):
            logging.getLogger(name).handlers = []
            logging.getLogger(name).propagate = True
    if not os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT"):
        return
    if os.environ.get("OTEL_EXPORTER_OTLP_PROTOCOL") == "grpc":
        from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
        from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
    else:
        from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
        from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
    from opentelemetry.sdk.metrics import MeterProvider
    from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
    from opentelemetry.sdk.resources import Resource
    from opentelemetry.sdk.trace import TracerProvider
    from opentelemetry.sdk.trace.export import BatchSpanProcessor

    resource = Resource.create(
        {"service.name": os.environ.get("OTEL_SERVICE_NAME", "kairo-agents")}
    )
    provider = TracerProvider(resource=resource)
    provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))
    trace.set_tracer_provider(provider)
    metrics.set_meter_provider(
        MeterProvider(
            resource=resource,
            metric_readers=[PeriodicExportingMetricReader(OTLPMetricExporter())],
        )
    )


def callback_headers() -> dict[str, str]:
    headers: dict[str, str] = {}
    inject(headers)
    return {key: value for key, value in headers.items() if key in ("traceparent", "tracestate")}


class TelemetryMiddleware:
    def __init__(self, app: Any):
        self.app = app

    async def __call__(self, scope: Any, receive: Callable, send: Callable) -> None:
        if scope["type"] != "http" or scope["path"] == "/health":
            await self.app(scope, receive, send)
            return
        headers = {
            key.decode("latin-1").lower(): value.decode("latin-1")
            for key, value in scope["headers"]
        }
        ids: dict[str, str] = {}
        for name in ("brand", "campaign", "run"):
            try:
                ids[name + "Id"] = str(UUID(headers.get(f"x-kairo-{name}-id", "")))
            except ValueError:
                pass
        token = correlation.set(ids)
        parent = TraceContextTextMapPropagator().extract(headers)
        with trace.get_tracer("Kairo").start_as_current_span(
            "agents.request",
            context=parent,
            kind=SpanKind.SERVER,
            record_exception=False,
            set_status_on_exception=False,
        ) as span:
            for name, value in ids.items():
                span.set_attribute("kairo." + name.replace("Id", "_id"), value)
            status = 500
            started = time.monotonic()

            async def respond(message: Any) -> None:
                nonlocal status
                if message["type"] == "http.response.start":
                    status = message["status"]
                await send(message)

            try:
                await self.app(scope, receive, respond)
            finally:
                route = getattr(scope.get("route"), "path", "unmatched")
                span.update_name("agents " + route)
                span.set_attribute("http.route", route)
                span.set_attribute("http.response.status_code", status)
                if status >= 500:
                    span.set_status(StatusCode.ERROR)
                metrics.get_meter("Kairo").create_histogram(
                    "kairo.agent.request.duration", "s"
                ).record(
                    time.monotonic() - started,
                    {"route": route, "outcome": str(status // 100) + "xx"},
                )
                logger.info("Agent request finished")
                correlation.reset(token)


class ObservedModels:
    def __init__(self, models: Any):
        self.models = models

    def __getattr__(self, name: str) -> Any:
        return getattr(self.models, name)

    async def invoke(self, operation: str, **kwargs: Any) -> Any:
        started = time.monotonic()
        outcome = "failed"
        with trace.get_tracer("Kairo").start_as_current_span(
            "model." + operation,
            record_exception=False,
            set_status_on_exception=False,
        ) as span:
            model = str(kwargs.get("model", "unknown")).rsplit("/", 1)[-1][:100]
            span.set_attribute("gen_ai.request.model", model)
            try:
                answer = await getattr(self.models, operation)(**kwargs)
                outcome = "succeeded"
                return answer
            except Exception:
                span.set_status(StatusCode.ERROR)
                raise
            finally:
                metrics.get_meter("Kairo").create_histogram("kairo.model.duration", "s").record(
                    time.monotonic() - started,
                    {"model": model, "operation": operation, "outcome": outcome},
                )

    async def generate_content(self, **kwargs: Any) -> Any:
        return await self.invoke("generate_content", **kwargs)

    async def generate_videos(self, **kwargs: Any) -> Any:
        return await self.invoke("generate_videos", **kwargs)


class ObservedClient:
    def __init__(self, client: Any):
        self.client = client
        self.aio = ObservedAsyncClient(client.aio)

    def __getattr__(self, name: str) -> Any:
        return getattr(self.client, name)


class ObservedAsyncClient:
    def __init__(self, client: Any):
        self.client = client
        self.models = ObservedModels(client.models)

    def __getattr__(self, name: str) -> Any:
        return getattr(self.client, name)
