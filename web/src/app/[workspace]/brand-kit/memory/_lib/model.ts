import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

export type Entry = components["schemas"]["MemoryEntryView"];
export type Detail = components["schemas"]["MemoryEntryDetail"];
export type MemoryState = FormState & { conflict?: boolean; detail?: Detail; removed?: boolean };
export const kinds = ["message", "visual", "audience", "region", "change"] as const;
export const outcomes = ["worked", "didnt_work"] as const;
export const states = ["active", "removed"] as const;
export const kindName = (value: string) => ({ message: "Message", visual: "Visual", audience: "Audience", region: "Region", change: "Change" })[value] ?? value;
export const outcomeName = (value: string) => ({ worked: "Worked", didnt_work: "Didn't work" })[value] ?? value;
export const usageName = (value: string) => ({ brief: "Brief", asset: "Asset", opportunity: "Opportunity ranking" })[value] ?? value;
export function languageName(value: string) {
  try { return new Intl.DisplayNames(["en"], { type: "language" }).of(value) ?? value; }
  catch { return value; }
}
export const validId = (value: unknown): value is string => typeof value === "string" && /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value);
export function sourceAnalysis(entry: Entry): string | null {
  if (entry.kind !== "change" || !entry.evidence || typeof entry.evidence !== "object" || Array.isArray(entry.evidence)) return null;
  const id = (entry.evidence as Record<string, unknown>).labAnalysisId;
  return validId(id) ? id : null;
}
export const validVersion = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value > 0 && value <= 2147483647;
export function filters(query: Record<string, string | string[] | undefined>) {
  const one = (value: string | string[] | undefined) => typeof value === "string" ? value : undefined;
  const kind = one(query.kind), outcome = one(query.outcome), state = one(query.state);
  return { kind: kinds.find(value => value === kind), outcome: outcomes.find(value => value === outcome), state: states.find(value => value === state) ?? "active" };
}
export function stamp(value: string | null, timeZone: string) {
  return value && Number.isFinite(Date.parse(value)) ? new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone }).format(new Date(value)) : "Date unavailable";
}
export function validateEdit(title: string, lesson: string): MemoryState | null {
  const fieldErrors: Record<string, string[]> = {};
  if (!title.trim() || title.trim().length > 120) fieldErrors.title = ["Write a title of 1 to 120 characters."];
  if (!lesson.trim() || lesson.trim().length > 500) fieldErrors.lesson = ["Write a lesson of 1 to 500 characters."];
  return Object.keys(fieldErrors).length ? { status: "error", fieldErrors } : null;
}
