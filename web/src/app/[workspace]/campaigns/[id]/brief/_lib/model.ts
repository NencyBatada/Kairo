import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

export type BriefDetail = components["schemas"]["BriefDetail"];
export type SmartBrief = components["schemas"]["SmartBriefView"];
export type Proposal = components["schemas"]["BriefProposalView"];
export type ProofResult = components["schemas"]["ProofPlanResult"];
export type BriefState = FormState & { detail?: BriefDetail; proposal?: Proposal };
export type EditRequest = components["schemas"]["BriefEditRequest"];

export const fields = [
  ["title", "Title"], ["objective", "Objective"], ["kpi", "KPI"], ["campaignTypes", "Campaign types"],
  ["budget", "Budget"], ["dates", "Dates"], ["regions", "Regions"], ["audiences", "Audiences"],
  ["keyMessages", "Key messages"], ["products", "Products"], ["landingPages", "Landing pages"], ["languages", "Languages"],
] as const;

export function version(value: FormDataEntryValue | null, minimum = 0) {
  if (typeof value !== "string" || !/^\d{1,10}$/.test(value)) return null;
  const number = Number(value);
  return number >= minimum && number <= 2147483647 ? number : null;
}

export function money(value: number | string | null | undefined, currency: string) {
  if (value == null || !Number.isFinite(Number(value))) return "Not available";
  try { return new Intl.NumberFormat("en-IN", { style: "currency", currency, maximumFractionDigits: 2 }).format(Number(value)); }
  catch { return `${value} ${currency}`; }
}

export function day(value: string | null) {
  return value ? new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`)) : "Not set";
}

export function percent(value: number | string | null) {
  return value == null || !Number.isFinite(Number(value)) ? "Not available" : `${(Number(value) * 100).toFixed(1)}%`;
}

export function readEdit(form: FormData): { body: EditRequest; errors: Record<string, string[]> } {
  const text = (name: string) => String(form.get(name) ?? "").trim();
  const list = (name: string) => text(name).split(/\r?\n/).map(value => value.trim()).filter(Boolean);
  const choices = (name: string) => form.getAll(name).filter((value): value is string => typeof value === "string");
  const errors: Record<string, string[]> = {};
  const body: EditRequest = {
    version: version(form.get("version")), briefVersion: version(form.get("briefVersion"), 1),
    title: text("title"), objective: text("objective"), kpi: text("kpi"), budget: text("budget"),
    startDate: text("startDate"), endDate: text("endDate"), campaignTypes: choices("campaignTypes"),
    regions: choices("regions"), audiences: list("audiences"), keyMessages: list("keyMessages"),
    productIds: choices("productIds"), landingPages: list("landingPages"), languages: choices("languages"),
  };
  if (body.version === null || body.briefVersion === null) errors.version = ["Reload the brief before saving."];
  if (!body.title || body.title.length > 120) errors.title = ["Enter a title of up to 120 characters."];
  if (!body.objective || body.objective.length > 300) errors.objective = ["Enter an objective of up to 300 characters."];
  if (!body.kpi) errors.kpi = ["Choose a KPI."];
  const budget = text("budget");
  if (!/^\d+(\.\d{1,2})?$/.test(budget) || Number(budget) <= 0 || Number(budget) > 999999999999) errors.budget = ["Enter a budget above zero, with up to two decimal places."];
  const validDay = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value) && Number(value.slice(0, 4)) >= 1 && Number(value.slice(0, 4)) <= 9999 && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
  for (const name of ["startDate", "endDate"] as const) {
    try { if (!validDay(body[name]!)) errors[name] = ["Choose a valid date."]; }
    catch { errors[name] = ["Choose a valid date."]; }
  }
  if (!errors.startDate && !errors.endDate && body.endDate! < body.startDate!) errors.endDate = ["Choose an end date on or after the start date."];
  for (const [name, maximum] of [["campaignTypes", 3], ["regions", 40], ["productIds", 12], ["languages", 5]] as const) {
    if (!body[name]!.length || body[name]!.length > maximum) errors[name] = [`Choose 1 to ${maximum} options.`];
  }
  for (const [name, limit] of [["audiences", 200], ["keyMessages", 150]] as const) {
    if (!body[name]!.length || body[name]!.length > 5 || body[name]!.some(item => item.length > limit)) errors[name] = [`Enter 1 to 5 lines of up to ${limit} characters each.`];
  }
  if (body.landingPages!.length > 10 || body.landingPages!.some(value => {
    try { const url = new URL(value); return url.protocol !== "https:" || !!url.username || !!url.password || value.length > 500; } catch { return true; }
  })) errors.landingPages = ["Enter up to 10 HTTPS addresses, one per line."];
  return { body, errors };
}
