import { room as base } from "../fixtures.test-data";
import type { Recommendation, Recommendations, Room, Settings } from "./_lib/model";

export const brandId = "11111111-1111-1111-1111-111111111111";
export const campaignId = "22222222-2222-2222-2222-222222222222";
export const recommendationId = "33333333-3333-3333-3333-333333333333";
export function room(overrides: Partial<Room> = {}): Room { return { ...base, id: campaignId, version: 7, title: "Rain-ready trainers", stage: "live", runs: [], pendingApproval: null, ...overrides }; }
export function recommendation(overrides: Partial<Recommendation> = {}): Recommendation { return { id: recommendationId, version: "3", source: "kairo", type: "target_change", campaignType: "search", state: "proposed", title: "Set the Search target CPA to INR 80", expectedEffect: "Google may bring more conversions at a higher cost.", reasoning: "The completed test achieved INR 80 per conversion.", change: { campaignType: "search", biddingStrategy: "target_cpa", currentTarget: "60", target: "80" }, evidence: { window: { from: "2026-10-10", to: "2026-10-15" }, campaigns: [{ campaignType: "search", cost: 1200, conversions: 15, totalBudget: 5000 }] }, duty: "target_changes", held: false, canApprove: true, problem: null, decidedBy: null, decidedAt: null, dismissReason: null, createdAt: "2026-10-16T06:00:00Z", updatedAt: "2026-10-16T06:00:00Z", ...overrides }; }
export function list(recommendations = [recommendation()], overrides: Partial<Recommendations> = {}): Recommendations { return { test: { stage: "results_ready", start: "2026-10-10", end: "2026-10-15", endedAt: "2026-10-16T00:00:00Z", endedEarly: false, running: false }, recommendations, ...overrides }; }
export const settings: Settings = { briefApprovers: [], assetApprovers: [], launchApprovers: [], budgetOwners: ["owner"], budgetMoveApprovers: ["priya"], targetChangeApprovers: ["priya"], googleSuggestionApprovers: [], budgetLimit: 50000, currency: "INR" };
export const members = [{ id: "priya", name: "Priya Shah" }, { id: "owner", name: "Budget Owner" }];
