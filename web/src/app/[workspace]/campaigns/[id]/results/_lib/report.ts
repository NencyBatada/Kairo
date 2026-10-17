import type { components } from "@/lib/core-api/schema";

export type Impact = components["schemas"]["ImpactView"];
export type Report = components["schemas"]["ImpactReportView"];
export type Reading = components["schemas"]["LiftReadingView"];
export type Memory = components["schemas"]["MemoryEntryView"];
export type Figure = { label: string; value: string; note?: string };

export function numeric(value: unknown): number | null {
  if (typeof value !== "number" && (typeof value !== "string" || !value.trim())) return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}
export const number = (value: number) => new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 }).format(value);
export const percent = (value: number) => `${value < 0 ? "−" : "+"}${Math.abs(value * 100).toFixed(1)}%`;
export function money(value: number, currency: string) {
  try { return new Intl.NumberFormat("en-IN", { style: "currency", currency, maximumFractionDigits: 2 }).format(value); }
  catch { return `${number(value)} ${currency}`; }
}
export function date(value: string) {
  const parsed = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(parsed.getTime()) ? new Intl.DateTimeFormat("en-IN", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(parsed) : "Date unavailable";
}
export function timestamp(value: string | null, timeZone: string) {
  if (!value || !Number.isFinite(Date.parse(value))) return "Date unavailable";
  return new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone }).format(new Date(value));
}
export function range(estimate: unknown, low: unknown, high: unknown) {
  const value = numeric(estimate), lower = numeric(low), upper = numeric(high);
  return value !== null && lower !== null && upper !== null && lower <= value && value <= upper ? { value, low: lower, high: upper } : null;
}
export const label = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[_-]/g, " ").replace(/^./, char => char.toUpperCase());
export const outcomeName = (value: string) => ({ worked: "Worked", no_effect: "No effect detected", inconclusive: "Inconclusive" })[value] ?? label(value);
export const nextName = (value: string) => ({ scale_up: "Scale up", change: "Change the approach", stop: "Stop", retest: "Run another test" })[value] ?? label(value);
export const flagName = (value: string) => ({ changed_after_lock: "Proof plan changed after launch", approved_with_warnings: "Proof plan approved with warnings", ended_early: "Test ended early", account_change: "Other account changes", short_window: "Short test window" })[value] ?? label(value);
export const reasonName = (value: string) => ({ thin_data: "Collect more outcome data", wide_range: "Narrow the confidence range", short_window: "Use a longer test window", few_regions: "Include more regions" })[value] ?? label(value);
export const readingReason = (value: string | null) => ({ no_test_days: "No test days are covered by the loaded data.", no_data: "The loaded data does not cover this test.", no_outcome: "Too few outcomes are available to estimate lift.", few_placebos: "There are too few comparison windows to estimate a reliable range.", insufficient_history: "There is not enough history to establish the comparison." })[value ?? ""] ?? "A complete estimate and confidence range are not available. Review the data coverage and proof plan.";
export function liftRange(reading: Reading | null) {
  return reading && !reading.early && reading.status === "ok" ? range(reading.lift, reading.low, reading.high) : null;
}
export function reportFigures(report: Report, reading: Reading | null): Figure[] {
  const usable = liftRange(reading);
  const extra = usable && reading ? range(reading.extra, reading.extraLow, reading.extraHigh) : null;
  const cost = numeric(report.costPerExtra), low = numeric(report.costPerExtraLow), high = numeric(report.costPerExtraHigh);
  const formatMoney = (value: unknown) => numeric(value) === null ? "Not available" : money(numeric(value)!, report.currency);
  return [
    { label: `Estimated extra ${label(report.kpi).toLowerCase()}`, value: extra ? number(extra.value) : "Not available", note: extra ? `90% range: ${number(extra.low)} to ${number(extra.high)}` : "A complete estimate and range are required." },
    { label: "Recorded ad spend", value: formatMoney(report.spend) },
    { label: "Recorded Google Ads conversions", value: numeric(report.results) === null ? "Not available" : number(numeric(report.results)!), note: "Recorded conversions are not measured extra results." },
    { label: "Cost per extra result", value: cost !== null && cost >= 0 ? money(cost, report.currency) : "Not available", note: cost === null ? "A positive extra-result estimate is needed." : low !== null && high !== null && low <= cost && cost <= high ? `90% range: ${money(low, report.currency)} to ${money(high, report.currency)}` : low !== null && low <= cost ? `90% range: from ${money(low, report.currency)}; no finite upper bound supplied.` : "A complete cost range is not available." },
    { label: "Cost per recorded conversion", value: formatMoney(report.costPerResult), note: "Spend divided by recorded Google Ads conversions." },
    { label: "Target cost", value: formatMoney(report.targetCost) },
  ];
}
