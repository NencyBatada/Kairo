import { context, propagation } from "@opentelemetry/api";

export function traceHeaders(): Record<string, string> {
  const result: Record<string, string> = {};
  propagation.inject(context.active(), result, {
    set(carrier, key, value) {
      if (key === "traceparent" || key === "tracestate") carrier[key] = value;
    },
  });
  return result;
}
