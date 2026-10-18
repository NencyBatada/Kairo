import { describe, expect, test, vi } from "vitest";
import { trace } from "@opentelemetry/api";
import { NodeSDK } from "@opentelemetry/sdk-node";
import { configureConsoleLogging, logRequestFailure, safeSpan, SafeTraceExporter } from "./telemetry";

describe("production telemetry", () => {
  test("real SDK spans retain export context after privacy filtering", async () => {
    const exported: Parameters<SafeTraceExporter["export"]>[0] = [];
    const exporter = new SafeTraceExporter({
      export(spans, callback) { exported.push(...spans); callback({ code: 0 }); },
      shutdown: async () => {},
      forceFlush: async () => {},
    });
    const sdk = new NodeSDK({ traceExporter: exporter, metricReaders: [], logRecordProcessors: [], instrumentations: [] });
    sdk.start();
    try {
      const span = trace.getTracer("test").startSpan("private");
      span.setAttribute("gen_ai.prompt", "private");
      span.setAttribute("http.response.status_code", 502);
      span.recordException(new Error("private"));
      span.end();
      await sdk.shutdown();
      expect(exported).toHaveLength(1);
      expect(exported[0].spanContext().traceId).toHaveLength(32);
      expect(exported[0].attributes["http.response.status_code"]).toBe(502);
      expect(exported[0].duration).toHaveLength(2);
      expect(exported[0].events).toEqual([]);
      expect(exported[0].name).toBe("web request");
    } finally {
      trace.disable();
    }
  });
  test("framework console output cannot leak request or exception contents", () => {
    const previous = { log: console.log, info: console.info, debug: console.debug, warn: console.warn, error: console.error };
    const write = vi.spyOn(process.stdout, "write").mockReturnValue(true);
    try {
      configureConsoleLogging();
      console.error(new Error("private"), { prompt: "private" });
      expect(write).toHaveBeenCalledTimes(1);
      const output = String(write.mock.calls[0][0]);
      expect(output).not.toContain("private");
      expect(JSON.parse(output)).toMatchObject({ severity: "ERROR", message: "Web log" });
    } finally {
      Object.assign(console, previous);
      write.mockRestore();
    }
  });
  test("exports route metadata without URLs, exceptions, credentials or prompts", () => {
    const spanContext = () => ({ traceId: "a".repeat(32), spanId: "b".repeat(16), traceFlags: 1 });
    const span = {
      name: "GET /api?code=private",
      attributes: {
        "http.route": "/api/[...path]",
        "http.response.status_code": 502,
        "next.span_type": "AppRouteRouteHandlers.runHandler",
        "url.full": "https://example.com?code=private",
        "http.request.header.authorization": "private",
        "gen_ai.prompt": "private",
      },
      events: [{ name: "exception", attributes: { "exception.message": "private" } }],
      links: [{ attributes: { "private": "private" } }],
      status: { code: 2, message: "private" },
      spanContext,
    } as unknown as Parameters<typeof safeSpan>[0];
    const result = safeSpan(span);
    expect(JSON.stringify(result)).not.toContain("private");
    expect(result.attributes["http.response.status_code"]).toBe(502);
    expect(result.spanContext()).toEqual(spanContext());
    expect(span.events).toHaveLength(1);
  });

  test("error logging never serializes request or error text", () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => {});
    try {
      logRequestFailure();
      expect(JSON.parse(log.mock.calls[0][0])).toMatchObject({ severity: "ERROR", message: "Web request failed" });
    } finally {
      log.mockRestore();
    }
  });
});
