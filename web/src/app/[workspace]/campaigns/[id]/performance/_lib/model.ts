import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

export type Pacing = components["schemas"]["PacingView"];
export type PacingCampaign = components["schemas"]["PacingCampaignView"];
export type Day = components["schemas"]["PacingDayView"];
export type StockLog = components["schemas"]["StockPauseLog"];
export type Pause = components["schemas"]["StockPauseView"];
export type Lift = components["schemas"]["LiftView"];
export type Reading = components["schemas"]["LiftReadingView"];
export type StockDecision = { kind: "undo"; id: string; version: number } | { kind: "confirm" | "dismiss"; id: string; context: string };
export type StockState = FormState & { conflict?: boolean; log?: StockLog };

export const channelName = (value: string) => ({ search: "Search", shopping: "Shopping", demand_gen: "Demand Gen" })[value] ?? value;
export const pauseName = (value: string) => ({ held: "Held for admin", paused: "Paused for stock", resumed: "Resumed on restock", undone: "Pause undone", dismissed: "Pause dismissed", cancelled: "Pause cancelled" })[value] ?? value;
export const paceName = (value: string) => ({ waiting: "Waiting for data", on_pace: "Within daily tolerance", under: "Under daily plan", over: "Over daily plan", under_pace: "Under daily plan", over_pace: "Over daily plan" })[value] ?? value;
export const money = (value: number | string, currency: string) => new Intl.NumberFormat("en-IN", { style: "currency", currency, maximumFractionDigits: 2 }).format(Number(value));
export const count = (value: number | string) => new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 }).format(Number(value));
export const date = (value: string) => new Intl.DateTimeFormat("en-IN", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
export const difference = (value: number | string) => `${Number(value) < 0 ? "−" : "+"}${Math.abs(Number(value) * 100).toFixed(1)}%`;
export const validId = (value: unknown): value is string => typeof value === "string" && /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value);
export const validVersion = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value > 0 && value <= 2147483647;

export function batchContext(log: StockLog, id: string) {
  return JSON.stringify([log.feedUpdatedAt, log.feedStale, log.pauses.filter(pause => pause.batchId === id && pause.state === "held").map(pause => [pause.id, pause.version]).sort((a, b) => String(a[0]).localeCompare(String(b[0])))]);
}

export function totals(days: Day[]) {
  return days.reduce((sum, day) => ({ spend: sum.spend + Number(day.spend), plan: sum.plan + Number(day.plannedSpend), conversions: sum.conversions + Number(day.conversions), value: sum.value + Number(day.conversionValue), clicks: sum.clicks + Number(day.clicks), impressions: sum.impressions + Number(day.impressions) }), { spend: 0, plan: 0, conversions: 0, value: 0, clicks: 0, impressions: 0 });
}

export function chartPaths(days: Day[], field: "spend" | "plannedSpend", x: (day: Day) => number, y: (value: number) => number) {
  return days.map((day, i) => `${i === 0 || Date.parse(day.date) - Date.parse(days[i - 1].date) > 86400000 ? "M" : "L"}${x(day)},${y(Number(day[field]))}`).join(" ");
}
