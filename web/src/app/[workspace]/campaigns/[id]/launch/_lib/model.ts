import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";
import type { BriefDetail, ProofResult } from "../../brief/_lib/model";

export type Room = components["schemas"]["CampaignDetail"];
export type PlanView = components["schemas"]["LaunchPlanView"];
export type Plan = components["schemas"]["LaunchPlan"];
export type Launch = components["schemas"]["LaunchStatus"];
export type Settings = components["schemas"]["LaunchSettings"];
export type LaunchState = FormState & { conflict?: boolean; view?: PlanView; launch?: Launch };
export type Draft = { channels: { campaignType: string; budget: string; biddingStrategy: string; target: string }[]; audienceIds: string; salesCountry: string; geoTargetIds: Record<string, string> };
export type Decision = { kind: "request" | "approve" | "retry" } | { kind: "send-back"; to: string; reason: string };
export const channelNames: Record<string, string> = { search: "Search", shopping: "Shopping", demand_gen: "Demand Gen" };
export const bidNames: Record<string, string> = { maximize_clicks: "Maximise clicks", target_cpa: "Target cost per acquisition", target_roas: "Target return on ad spend" };
export const stageNames: Record<string, string> = { ready_to_launch: "Launch settings", asset_review: "Asset review", creating: "Creative Studio", brief_draft: "Brief" };
export function regions(proof: ProofResult) { return [...(proof.plan?.testRegions ?? []), ...(proof.plan?.holdouts ?? [])]; }
export function toDraft(plan: Plan, types: string[], proof: ProofResult): Draft {
  return { channels: types.map(type => { const channel = plan.settings.channels.find(row => row.campaignType === type); return { campaignType: type, budget: channel ? String(channel.budget) : "", biddingStrategy: channel?.biddingStrategy ?? "maximize_clicks", target: channel?.target == null ? "" : String(channel.target) }; }), audienceIds: (plan.settings.audienceIds ?? []).join("\n"), salesCountry: plan.settings.salesCountry ?? "", geoTargetIds: Object.fromEntries(regions(proof).map(region => [region.id, plan.settings.geoTargetIds?.[region.id] ?? (/^[1-9]\d*$/.test(region.id) ? region.id : "")])) };
}
function cents(value: unknown) {
  if (typeof value !== "string" || !/^\d{1,10}(\.\d{1,2})?$/.test(value)) return null;
  const [whole, fraction = ""] = value.split(".");
  return BigInt(whole) * BigInt(100) + BigInt(fraction.padEnd(2, "0"));
}
export function readSettings(input: unknown, plan: Plan, brief: BriefDetail, proof: ProofResult): { settings?: Settings; errors: Record<string, string[]> } {
  const errors: Record<string, string[]> = {};
  if (!input || typeof input !== "object") return { errors: { settings: ["Reload the launch settings before saving."] } };
  const draft = input as Draft;
  const types = brief.brief?.campaignTypes ?? [];
  if (!Array.isArray(draft.channels) || draft.channels.length !== types.length || !types.length || types.length > 3 || draft.channels.some(row => !row || typeof row !== "object" || !types.includes(row.campaignType) || !channelNames[row.campaignType]) || new Set(draft.channels.map(row => row.campaignType)).size !== types.length) return { errors: { settings: ["Set each campaign type from the approved brief exactly once."] } };
  const maximum = BigInt(100_000_000_000);
  const total = cents(String(plan.budget));
  let sum = BigInt(0);
  const days = Math.floor((Date.parse(`${plan.end}T00:00:00Z`) - Date.parse(`${plan.start}T00:00:00Z`)) / 86400000) + 1;
  const channels = draft.channels.map((row, index) => {
    const budget = cents(row.budget);
    if (budget === null || budget <= BigInt(0) || budget > maximum || (total !== null && budget > total) || !Number.isSafeInteger(days) || days < 1 || (budget !== null && budget * BigInt(10000) / BigInt(Math.max(1, days)) < BigInt(1))) errors[`channels.${index}.budget`] = ["Enter a positive channel budget within the approved total, with up to two decimal places."];
    else sum += budget;
    if (!Object.hasOwn(bidNames, row.biddingStrategy) || (row.campaignType === "shopping" && row.biddingStrategy === "target_cpa")) errors[`channels.${index}.biddingStrategy`] = ["Choose a supported bidding strategy for this channel."];
    const target = typeof row.target === "string" && /^\d{1,10}(\.\d{1,6})?$/.test(row.target) ? Number(row.target) : NaN;
    if (row.biddingStrategy !== "maximize_clicks" && (!Number.isFinite(target) || target <= 0 || target > 1_000_000_000)) errors[`channels.${index}.target`] = ["Enter a positive bid target up to one billion."];
    return { campaignType: row.campaignType, budget: row.budget, biddingStrategy: row.biddingStrategy, target: row.biddingStrategy === "maximize_clicks" ? null : row.target };
  });
  if (total === null || sum !== total) errors.channels = ["Channel budgets must add up exactly to the approved brief budget."];
  const audienceIds = typeof draft.audienceIds === "string" ? draft.audienceIds.split(/[\s,]+/u).filter(Boolean) : [];
  const validId = (id: unknown): id is string => typeof id === "string" && /^[1-9]\d{0,18}$/.test(id) && BigInt(id) <= BigInt("9223372036854775807");
  if (typeof draft.audienceIds !== "string" || audienceIds.some(id => !validId(id)) || (types.includes("demand_gen") && !audienceIds.length)) errors.audienceIds = ["Enter Google Ads user-list IDs, one per line. Demand Gen needs at least one."];
  const country = typeof draft.salesCountry === "string" ? draft.salesCountry.trim().toUpperCase() : "";
  if (types.includes("shopping") && !/^[A-Z]{2}$/.test(country)) errors.salesCountry = ["Enter the two-letter Shopping sales country, such as IN."];
  const geo: Record<string, string> = {};
  const options = regions(proof);
  if (!draft.geoTargetIds || typeof draft.geoTargetIds !== "object" || Array.isArray(draft.geoTargetIds)) errors.settings = ["Resolve the test and holdout regions before saving."];
  else {
    for (const region of options) { const value = draft.geoTargetIds[region.id]; if (!validId(value)) errors[`geo.${region.id}`] = [`Enter the Google Ads location ID for ${region.name}.`]; else geo[region.id] = value; }
    if (Object.keys(draft.geoTargetIds).some(key => !options.some(region => region.id === key))) errors.settings = ["Only the approved test and holdout regions can be mapped."];
    const tests = (proof.plan?.testRegions ?? []).map(region => geo[region.id]);
    if ((proof.plan?.holdouts ?? []).some(region => geo[region.id] && tests.includes(geo[region.id]))) errors.settings = ["Test and holdout locations must not overlap."];
  }
  if (!proof.plan || !proof.plan.testRegions.length || !proof.plan.holdouts.length) errors.settings = ["Complete the approved test plan before launch."];
  return Object.keys(errors).length ? { errors } : { settings: { channels, audienceIds: [...new Set(audienceIds)], salesCountry: country || null, geoTargetIds: geo }, errors };
}
export function context(room: Room, view: PlanView) { return JSON.stringify([room.version, room.stage, room.pendingApproval, room.actions, view.version, view.hash, view.blocker]); }
export function validateDecision(input: unknown): input is Decision {
  if (!input || typeof input !== "object" || !("kind" in input)) return false;
  if (["request", "approve", "retry"].includes(String(input.kind))) return true;
  if (input.kind !== "send-back" || !("to" in input) || !Object.hasOwn(stageNames, String(input.to)) || !("reason" in input) || typeof input.reason !== "string") return false;
  const text = input.reason.trim().replace(/\s+/gu, " ");
  return text.length >= 2 && text.length <= 2000;
}
export function stepName(value: string) {
  const [kind] = value.split(":");
  return ({ queued: "Waiting to start", preflight: "Checking accounts and launch requirements", "upload-session": "Preparing YouTube upload", upload: "Uploading video to YouTube", media: "Adding media to Google Ads", build: "Creating paused campaigns", activate: "Activating all campaigns", complete: "Launch complete" } as Record<string, string>)[kind] ?? "Processing launch step";
}
export const statusNames: Record<string, string> = { queued: "Queued", running: "Launching", retry_wait: "Waiting to retry", failed: "Needs attention", live: "Live" };
