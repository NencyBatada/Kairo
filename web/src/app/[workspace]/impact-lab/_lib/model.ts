import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

export type Analysis = components["schemas"]["LabAnalysisView"];
export type Summary = components["schemas"]["LabAnalysisSummary"];
export type Setup = components["schemas"]["LabSetupRequest"];
export type Selection = components["schemas"]["LabSelectionRequest"];
export type Day = components["schemas"]["LabDayView"];
export type LabState = FormState & { analysis?: Analysis; conflict?: boolean; signIn?: boolean };
export type Operation = "setup" | "load" | "selection" | "run" | "memory";
export const sourceNames: Record<string, string> = { google_ads: "Google Ads", ga4: "Google Analytics 4", bigquery: "BigQuery", file: "CSV file" };
export const stateNames: Record<string, string> = { setup: "Setup", data_check: "Data check", running: "Running", result: "Result", failed: "Failed" };
export const outcomeNames: Record<string, string> = { rose: "Outcome increased", fell: "Outcome decreased", inconclusive: "Inconclusive" };
export const grainNames: Record<string, string> = { state: "State", city: "City", campaign: "Campaign", category: "Category", product: "Product", series: "Series" };
export const catalog: Record<string, Record<string, string[]>> = {
  google_ads: Object.fromEntries(["state", "city", "campaign", "category", "product"].map(grain => [grain, ["conversions", "conversion_value", "clicks"]])),
  ga4: { state: ["transactions", "purchase_revenue", "sessions", "key_events"], city: ["transactions", "purchase_revenue", "sessions", "key_events"], category: ["items_purchased", "item_revenue"], product: ["items_purchased", "item_revenue"] },
};
export const validId = (value: unknown): value is string => typeof value === "string" && /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value);
export const validVersion = (value: unknown) => (typeof value === "number" || typeof value === "string" && !!value.trim()) && Number.isInteger(Number(value)) && Number(value) > 0 && Number(value) <= 2147483647;
export function numeric(value: unknown): number | null {
  if (typeof value !== "number" && (typeof value !== "string" || !value.trim())) return null;
  return Number.isFinite(Number(value)) ? Number(value) : null;
}
export const number = (value: number) => new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 }).format(value);
export const percent = (value: number) => `${value < 0 ? "−" : "+"}${Math.abs(value * 100).toFixed(1)}%`;
export const label = (value: string) => value.replace(/_/g, " ").replace(/^./, char => char.toUpperCase());
export function isoDate(value: unknown): value is string {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
}
export const date = (value: string) => isoDate(value) ? new Intl.DateTimeFormat("en-IN", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(value)) : "Date unavailable";
export function indiaToday(now = new Date()) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Kolkata", year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
}
export function range(value: unknown, low: unknown, high: unknown) {
  const estimate = numeric(value), lower = numeric(low), upper = numeric(high);
  return estimate !== null && lower !== null && upper !== null && lower <= estimate && estimate <= upper ? { value: estimate, low: lower, high: upper } : null;
}
export function lift(value: Pick<Summary, "lift" | "low" | "high">) {
  const complete = range(value.lift, value.low, value.high);
  return complete ? `${percent(complete.value)} (90% range: ${percent(complete.low)} to ${percent(complete.high)})` : "A complete estimate and range are not available.";
}
export function amount(value: unknown, currency: string | null) {
  const parsed = numeric(value);
  if (parsed === null) return "Not available";
  if (currency) {
    try { return new Intl.NumberFormat("en-IN", { style: "currency", currency, maximumFractionDigits: 2 }).format(parsed); }
    catch { return `${number(parsed)} ${currency}`; }
  }
  return number(parsed);
}
export function dataCurrency(analysis: Analysis) {
  return ["conversion_value", "purchase_revenue", "item_revenue"].includes(analysis.source.metric) ? analysis.data?.currency ?? null : null;
}
export function setupOf(analysis?: Analysis): Setup {
  const { kind, metric, grain, table, dateColumn, valueColumn, seriesColumn } = analysis?.source ?? { kind: "google_ads", metric: "conversions", grain: "state", table: null, dateColumn: null, valueColumn: null, seriesColumn: null };
  return { version: analysis?.version ?? null, name: analysis?.name ?? "", whatChanged: analysis?.whatChanged ?? "", start: analysis?.start ?? "", end: analysis?.end ?? "", affectsKind: analysis?.affectsKind ?? "regions", source: { kind, metric, grain, table, dateColumn, valueColumn, seriesColumn } };
}
export function setupErrors(setup: Setup, today = indiaToday()) {
  const errors: Record<string, string[]> = {}, source = setup.source;
  if (!setup.name?.trim() || setup.name.trim().length > 200) errors.name = ["Name the change in 1 to 200 characters."];
  if (!setup.whatChanged?.trim() || setup.whatChanged.trim().length > 1000) errors.whatChanged = ["Describe what changed in 1 to 1000 characters."];
  if (!["regions", "products", "other"].includes(setup.affectsKind ?? "")) errors.affectsKind = ["Choose regions, products or other."];
  if (!isoDate(setup.start)) errors.start = ["Say when the change started."];
  else if (setup.start >= today) errors.start = ["The change must have started before today in India."];
  if (!isoDate(setup.end)) errors.end = ["Say when the change ended."];
  else if (isoDate(setup.start) && setup.end < setup.start) errors.end = ["The end can't be before the start."];
  else if (isoDate(setup.start) && (Date.parse(setup.end) - Date.parse(setup.start)) / 86400000 + 1 > 180) errors.end = ["Measure at most 180 days of the change."];
  if (!source || !sourceNames[source.kind ?? ""]) errors["source.kind"] = ["Choose an outcome source."];
  else if (catalog[source.kind!]) {
    const metrics = catalog[source.kind!][source.grain ?? ""];
    if (!metrics) errors["source.grain"] = ["Choose a breakdown available for this source."];
    if (!metrics?.includes(source.metric ?? "")) errors["source.metric"] = ["Choose an outcome available for this breakdown."];
  } else {
    if (!source.metric?.trim() || source.metric.trim().length > 60) errors["source.metric"] = ["Name the outcome in 1 to 60 characters."];
    if (source.kind === "bigquery") {
      if (!/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_]+$/.test(source.table?.trim() ?? "")) errors["source.table"] = ["Use a full project.dataset.table name."];
      for (const key of ["dateColumn", "valueColumn", "seriesColumn"] as const) if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(source[key]?.trim() ?? "")) errors[`source.${key}`] = ["Use a column name containing letters, numbers and underscores."];
    }
  }
  return errors;
}
export function selectionErrors(affected: string[], comparison: string[] | null, series: string[]) {
  if (new Set(affected).size < 1 || affected.length > 50) return "Pick 1 to 50 affected series.";
  if (comparison && comparison.length > 50) return "Pick at most 50 comparison series.";
  if ([...affected, ...comparison ?? []].some(key => !series.includes(key))) return "Pick series from the loaded data.";
  if (comparison?.some(key => affected.includes(key))) return "A series can't be both affected and a comparison.";
  return null;
}
export function sameKeys(a: string[], b: string[]) { return a.length === b.length && [...a].sort().every((key, index) => key === [...b].sort()[index]); }
export function canRemember(analysis: Analysis) { return analysis.state === "result" && analysis.result?.conclusive === true && ["rose", "fell"].includes(analysis.result.outcome) && !analysis.memoryEntryId; }
export function dailyRows(days: Day[]) {
  return days.filter(day => isoDate(day.date)).sort((a, b) => a.date.localeCompare(b.date));
}
export function csv(days: Day[]) {
  const cell = (value: unknown) => numeric(value) === null ? "" : String(numeric(value));
  return "\uFEFFdate,actual,expected,during_change\r\n" + dailyRows(days).map(day => [day.date, cell(day.actual), cell(day.expected), day.during ? "true" : "false"].join(",")).join("\r\n") + "\r\n";
}
export const sampleCsv = "date,series,value\r\n2026-01-01,Mumbai,120\r\n2026-01-01,Pune,100\r\n2026-01-02,Mumbai,125\r\n2026-01-02,Pune,103\r\n";
