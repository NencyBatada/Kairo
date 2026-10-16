import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

export type Recommendation = components["schemas"]["RecommendationView"];
export type Recommendations = components["schemas"]["RecommendationList"];
export type Room = components["schemas"]["CampaignDetail"];
export type Settings = components["schemas"]["ApprovalSettings"];
export type Member = { id: string; name: string };
export type CareState = FormState & { conflict?: boolean; recommendation?: Recommendation; room?: Room };
export type Decision = { kind: "approve" } | { kind: "dismiss"; reason: string };
export const channelName = (value: string) => ({ search: "Search", shopping: "Shopping", demand_gen: "Demand Gen" })[value] ?? value;
export const money = (value: number, currency: string) => new Intl.NumberFormat("en-IN", { style: "currency", currency, maximumFractionDigits: 2 }).format(value);
export const number = (value: number) => new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 }).format(value);
export const date = (value: string) => new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
export const label = (value: string) => (value === value.toUpperCase() ? value.toLowerCase() : value).replace(/([a-z])([A-Z])/g, "$1 $2").replaceAll("_", " ").replace(/\bcpa\b/gi, "CPA").replace(/\broas\b/gi, "ROAS").replace(/^./, letter => letter.toUpperCase());
export const object = (value: unknown): Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
export const numeric = (value: unknown): number | null => (typeof value === "number" || typeof value === "string" && value.trim() !== "") && Number.isFinite(Number(value)) ? Number(value) : null;
export const validId = (value: unknown): value is string => typeof value === "string" && /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value);
export const validVersion = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value > 0 && value <= 2147483647;

export function validateDecision(value: unknown): CareState | null {
  const decision = object(value);
  if (typeof decision.kind !== "string" || !["approve", "dismiss"].includes(decision.kind)) return { status: "error", message: "Choose approve or dismiss." };
  if (decision.kind === "dismiss" && (typeof decision.reason !== "string" || decision.reason.trim().length > 500)) return { status: "error", message: "Keep the dismissal reason to 500 characters or fewer.", fieldErrors: { reason: ["Keep the dismissal reason to 500 characters or fewer."] } };
  return null;
}

export function approverNames(rec: Recommendation, settings: Settings, members: Member[]) {
  const ids = rec.duty === "budget_moves" ? settings.budgetMoveApprovers : rec.duty === "target_changes" ? settings.targetChangeApprovers : settings.googleSuggestionApprovers;
  const budgetOwner = rec.source === "google" && ["CAMPAIGN_BUDGET", "FORECASTING_CAMPAIGN_BUDGET", "MARGINAL_ROI_CAMPAIGN_BUDGET"].includes(rec.type);
  const assigned = budgetOwner ? settings.budgetOwners.filter(id => !ids.length || ids.includes(id)) : ids;
  return assigned.length ? assigned.map(id => members.find(member => member.id === id)?.name ?? id).join(", ") : budgetOwner ? "No eligible budget owner assigned. Ask an admin to check approval settings." : "Any workspace approver";
}

export function changeRows(rec: Recommendation, currency: string): { label: string; value: string }[] {
  const change = object(rec.change), rows: { label: string; value: string }[] = [];
  if (rec.type === "budget_move" && Array.isArray(change.budgets)) {
    const evidence = object(rec.evidence), campaigns = Array.isArray(evidence.campaigns) ? evidence.campaigns.map(object) : [];
    for (const item of change.budgets) { const budget = object(item), total = numeric(budget.total); if (typeof budget.campaignType === "string" && total !== null) { const previous = numeric(campaigns.find(campaign => campaign.campaignType === budget.campaignType)?.totalBudget); rows.push({ label: `${channelName(budget.campaignType)} total budget`, value: `${previous === null ? "Current not supplied" : money(previous, currency)} → ${money(total, currency)}` }); } }
    if (numeric(change.amount) !== null && typeof change.from === "string" && typeof change.to === "string") rows.unshift({ label: "Budget move", value: `${money(numeric(change.amount)!, currency)} from ${channelName(change.from)} to ${channelName(change.to)}` });
  } else if (rec.type === "target_change") {
    const format = (value: unknown) => { const amount = numeric(value); return amount === null ? "Not supplied" : change.biddingStrategy === "target_roas" ? `${number(amount * 100)}% return on ad spend` : change.biddingStrategy === "target_cpa" ? `${money(amount, currency)} per conversion` : number(amount); };
    rows.push({ label: "Current target", value: format(change.currentTarget) }, { label: "Proposed target", value: format(change.target) });
  } else if (rec.source === "google") rows.push({ label: "Google Ads suggestion", value: label(rec.type) });
  return rows;
}

export function context(rec: Recommendation, test: Recommendations["test"], version: number | string) { return JSON.stringify([version, test, rec]); }
