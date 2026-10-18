import { trace } from "@opentelemetry/api";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";

type ExportedSpan = Parameters<OTLPTraceExporter["export"]>[0][number];

const allowedAttributes = new Set([
  "http.request.method", "http.method", "http.response.status_code", "http.status_code",
  "http.route", "next.route", "next.span_type",
]);

export function safeSpan(span: ExportedSpan): ExportedSpan {
  return Object.assign(Object.create(span), {
    name: "web " + (span.attributes["next.span_type"] ?? "request"),
    attributes: Object.fromEntries(Object.entries(span.attributes).filter(([key]) => allowedAttributes.has(key))),
    events: [],
    links: [],
    status: { code: span.status.code },
  });
}

export class SafeTraceExporter {
  constructor(private readonly exporter: Pick<OTLPTraceExporter, "export" | "shutdown" | "forceFlush"> = new OTLPTraceExporter()) {}

  export(spans: Parameters<OTLPTraceExporter["export"]>[0], callback: Parameters<OTLPTraceExporter["export"]>[1]): void {
    this.exporter.export(spans.map(safeSpan), callback);
  }

  shutdown(): Promise<void> { return this.exporter.shutdown(); }
  forceFlush(): Promise<void> { return this.exporter.forceFlush(); }
}

function logFields(severity: string, message: string) {
  const span = trace.getActiveSpan()?.spanContext();
  const project = process.env.GOOGLE_CLOUD_PROJECT;
  return {
    severity,
    message,
    ...(span ? {
      traceId: span.traceId,
      spanId: span.spanId,
      ...(project ? { "logging.googleapis.com/trace": `projects/${project}/traces/${span.traceId}` } : {}),
      "logging.googleapis.com/spanId": span.spanId,
    } : {}),
  };
}

export function configureConsoleLogging(): void {
  for (const [method, severity] of Object.entries({ log: "INFO", info: "INFO", debug: "DEBUG", warn: "WARNING", error: "ERROR" }) as ["log" | "info" | "debug" | "warn" | "error", string][]) {
    console[method] = () => { process.stdout.write(JSON.stringify(logFields(severity, "Web log")) + "\n"); };
  }
}

export function logRequestFailure(): void {
  if (process.env.K_SERVICE) {
    process.stdout.write(JSON.stringify(logFields("ERROR", "Web request failed")) + "\n");
  } else {
    console.error(JSON.stringify(logFields("ERROR", "Web request failed")));
  }
}
