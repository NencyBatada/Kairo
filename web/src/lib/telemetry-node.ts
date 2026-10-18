import { configureConsoleLogging, SafeTraceExporter } from "./telemetry";

export async function startTelemetry() {
  if (process.env.K_SERVICE) configureConsoleLogging();
  if (!process.env.OTEL_EXPORTER_OTLP_ENDPOINT) return;
  const [{ NodeSDK }, { W3CTraceContextPropagator }] = await Promise.all([
    import("@opentelemetry/sdk-node"),
    import("@opentelemetry/core"),
  ]);
  const protocol = process.env.OTEL_EXPORTER_OTLP_TRACES_PROTOCOL ?? process.env.OTEL_EXPORTER_OTLP_PROTOCOL;
  const { OTLPTraceExporter } = protocol === "grpc"
    ? await import("@opentelemetry/exporter-trace-otlp-grpc")
    : protocol === "http/json"
      ? await import("@opentelemetry/exporter-trace-otlp-http")
      : await import("@opentelemetry/exporter-trace-otlp-proto");
  const sdk = new NodeSDK({
    serviceName: process.env.OTEL_SERVICE_NAME ?? "kairo-web",
    traceExporter: new SafeTraceExporter(new OTLPTraceExporter()),
    textMapPropagator: new W3CTraceContextPropagator(),
    logRecordProcessors: [],
    instrumentations: [],
  });
  sdk.start();
  process.once("SIGTERM", () => { void sdk.shutdown(); });
}
