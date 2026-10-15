import type { components } from "@/lib/core-api/schema";
import { text, type FormState } from "@/lib/forms";

export type Rule = components["schemas"]["RuleView"];
export type RuleRequest = components["schemas"]["RuleRequest"];
export type RuleFormState = FormState & { preview?: { request: RuleRequest; description: string } };
export type RuleAction = (previous: RuleFormState, form: FormData) => Promise<RuleFormState>;

export function readRule(form: FormData) {
  const errors: Record<string, string[]> = {};
  const numeric = (key: string, integer = true) => {
    const raw = text(form, key);
    const value = Number(raw);
    if (!raw || !Number.isFinite(value) || (integer && !Number.isSafeInteger(value))) errors[key] = ["Enter a valid number."];
    return value;
  };
  const source = text(form, "source");
  const name = text(form, "name");
  const category = text(form, "category");
  if (name.length < 2 || name.length > 80) errors.name = ["Use 2 to 80 characters."];
  if (!category || category.length > 750) errors.category = ["Choose a product category."];
  if (!["weather", "festival", "stock"].includes(source)) errors.source = ["Choose weather, festival or stock."];
  const condition: components["schemas"]["ConditionRequest"] = { kind: null, threshold: null, withinDays: null, eventId: null, daysBefore: null, minUnits: null };
  if (source === "weather") {
    condition.kind = text(form, "condition.kind");
    condition.threshold = numeric("condition.threshold", false);
    condition.withinDays = numeric("condition.withinDays");
  } else if (source === "festival") {
    condition.eventId = text(form, "condition.eventId") || null;
    condition.daysBefore = numeric("condition.daysBefore");
  } else if (source === "stock") condition.minUnits = numeric("condition.minUnits");
  const request: RuleRequest = { name, source, category, condition, regions: [...new Set(form.getAll("regions").filter((value): value is string => typeof value === "string"))], cooldownHours: numeric("cooldownHours") };
  return { request, errors };
}

export function outcomeLabel(outcome: string) {
  const labels: Record<string, string> = { fired: "Opportunity triggered", quiet: "Condition not met", cooling_down: "Waiting for cooldown", no_stock: "No matching products in stock", needs_quantities: "Stock quantities needed", weather_unavailable: "Forecast unavailable", event_passed: "Event has passed", event_missing: "Event no longer available", failed: "Check failed", created: "Created opportunity", joined: "Added to an existing opportunity" };
  return labels[outcome] ?? outcome.replaceAll("_", " ");
}
