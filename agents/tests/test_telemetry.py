import asyncio
import json
import logging
from types import SimpleNamespace
from uuid import uuid4

from fastapi import FastAPI
from fastapi.testclient import TestClient
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter

from kairo_agents.agents.ask.telemetry import AskModelTelemetry
from kairo_agents.telemetry import (
    CloudFormatter,
    ObservedModels,
    TelemetryMiddleware,
    callback_headers,
    correlation,
)

provider = TracerProvider()
exporter = InMemorySpanExporter()
provider.add_span_processor(SimpleSpanProcessor(exporter))
trace.set_tracer_provider(provider)


def test_request_preserves_trace_and_callback_context_without_private_data():
    app = FastAPI()
    app.add_middleware(TelemetryMiddleware)
    brand = str(uuid4())

    @app.post("/test")
    async def endpoint():
        return {"headers": callback_headers(), "ids": correlation.get()}

    exporter.clear()
    with TestClient(app) as client:
        answer = client.post(
            "/test?token=private",
            json={"prompt": "private"},
            headers={
                "traceparent": "00-" + "a" * 32 + "-" + "b" * 16 + "-01",
                "x-kairo-brand-id": brand,
                "x-kairo-campaign-id": "private",
                "authorization": "private",
                "baggage": "password=private",
            },
        )
    assert answer.status_code == 200
    assert answer.json()["ids"] == {"brandId": brand}
    assert answer.json()["headers"]["traceparent"].split("-")[1] == "a" * 32
    assert "baggage" not in answer.json()["headers"]
    span = exporter.get_finished_spans()[-1]
    assert span.parent.span_id == int("b" * 16, 16)
    assert span.name == "agents /test"
    assert "private" not in str(span.attributes)
    assert correlation.get() is None


def test_cloud_logs_drop_format_arguments_exception_text_and_third_party_messages():
    record = logging.LogRecord(
        "kairo_agents.test",
        logging.ERROR,
        "",
        1,
        "Failed for %s",
        ("private",),
        (ValueError, ValueError("private"), None),
    )
    fields = json.loads(CloudFormatter().format(record))
    assert fields["message"] == "Failed for %s"
    assert fields["exceptionType"] == "ValueError"
    assert "private" not in json.dumps(fields)
    record.name = "uvicorn.access"
    record.msg = "private"
    assert "private" not in CloudFormatter().format(record)


def test_model_spans_measure_failures_without_recording_payload_or_exception():
    async def fail(**kwargs):
        raise RuntimeError("private")

    async def run():
        models = ObservedModels(SimpleNamespace(generate_content=fail))
        try:
            await models.generate_content(model="gemini-test", contents="private")
        except RuntimeError:
            pass
        else:
            raise AssertionError("Model errors must propagate")

    exporter.clear()
    asyncio.run(run())
    span = exporter.get_finished_spans()[-1]
    assert span.name == "model.generate_content"
    assert span.status.status_code == trace.StatusCode.ERROR
    assert span.attributes["gen_ai.request.model"] == "gemini-test"
    assert not span.events
    assert "private" not in str(span.attributes)


def test_adk_model_callbacks_measure_calls_and_close_interrupted_spans():
    exporter.clear()
    telemetry = AskModelTelemetry("gemini-test")
    telemetry.before(None, None)
    telemetry.after(None, None)
    telemetry.before(None, None)
    telemetry.error(None, None, ValueError("private"))
    telemetry.finish("failed")
    spans = exporter.get_finished_spans()
    assert len(spans) == 2
    assert spans[0].status.status_code == trace.StatusCode.UNSET
    assert spans[1].status.status_code == trace.StatusCode.ERROR
    assert not spans[1].events
