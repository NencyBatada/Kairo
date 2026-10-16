import { asset, assetSet, campaignId } from "../assets/fixtures.test-data";
import { room } from "../fixtures.test-data";
import type { Asset } from "../assets/_lib/model";
import type { Room, Issue } from "./_lib/review";

export const issue: Issue = { rule: "brand.voice", ruleName: "Brand voice", severity: "warn", message: "The headline uses a voice the brand avoids.", field: "headlines", index: 0, at: null, evidence: "Hurry now", overridable: true };
export function flagged(overrides: Partial<Asset> = {}): Asset {
  return asset({ checkState: "failed", canOverride: true, canSendBack: true, check: { state: "failed", score: 70, passed: false, settingsVersion: 1, checkedAt: "2026-10-16T06:00:00Z", issues: [issue], overridden: null, fixAttempts: 3, fixes: [] }, ...overrides });
}
export function reviewRoom(overrides: Partial<Room> = {}): Room {
  return { ...room, id: campaignId, stage: "asset_review", runs: [], status: "waiting_approval", pendingApproval: { gate: "assets", label: "Asset approval", route: "approvers", routeReason: null, requestedAt: "2026-10-16T06:00:00Z", requestedBy: room.owner, approvers: [{ id: "reviewer-1", name: "Priya Shah" }] }, actions: { ...room.actions, requestApproval: null, approve: null, sendBackTo: ["creating"], retryRun: null, approveBlocker: "1 asset still has open issues.", discard: false }, ...overrides };
}
export function reviewSet(entries = [flagged()]) {
  return assetSet(entries, { stage: "asset_review", canEdit: false, canSendBack: true, check: { score: 70, passed: 0, flagged: entries.length, unchecked: 0, overridden: 0, settingsVersion: 1, passScore: 80, fixAttempts: 3, approveBlocker: "1 asset still has open issues." } });
}
