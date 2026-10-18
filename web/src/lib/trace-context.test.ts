import { propagation } from "@opentelemetry/api";
import { expect, test, vi } from "vitest";
import { traceHeaders } from "./trace-context";

test("only trace context can cross the server boundary", () => {
  const inject = vi.spyOn(propagation, "inject").mockImplementation((_context, carrier, setter) => {
    setter?.set(carrier, "traceparent", "trace-value");
    setter?.set(carrier, "tracestate", "state-value");
    setter?.set(carrier, "baggage", "private");
  });
  try {
    expect(traceHeaders()).toEqual({ traceparent: "trace-value", tracestate: "state-value" });
  } finally {
    inject.mockRestore();
  }
});
