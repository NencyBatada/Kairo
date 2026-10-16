import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";
import type { Asset, AssetSet } from "../../assets/_lib/model";

export type Room = components["schemas"]["CampaignDetail"];
export type Issue = components["schemas"]["CheckIssueView"];
export type ReviewDecision = { kind: "check" | "request" | "approve" } | { kind: "override"; assetId: string; assetVersion: number | string; reason: string } | { kind: "send-back"; assetIds: string[]; reason: string };
export type ReviewState = FormState & { conflict?: boolean };
export const checkLabels: Record<string, string> = { passed: "Passed", failed: "Flagged", overridden: "Overridden", unchecked: "Not checked" };
const fields: Record<string, string> = { headlines: "Headline", longHeadlines: "Long headline", descriptions: "Description", keywords: "Keyword", script: "Script line", title: "Shopping title", path1: "First display path", path2: "Second display path", businessName: "Business name", callToAction: "Call to action" };

export function issueLocation(issue: Issue) {
  const field = issue.field ? fields[issue.field] ?? issue.field : "";
  const index = issue.index === null ? "" : ` ${Number(issue.index) + 1}`;
  const at = issue.at === null ? "" : `${issue.at} seconds`;
  return [field ? `${field}${index}` : "", at].filter(Boolean).join(" · ");
}

export function snapshot(room: Room, set: AssetSet) {
  return JSON.stringify([room.version, room.actions, room.pendingApproval, set]);
}

export function matches(asset: Asset, state: string, language: string) {
  return (!state || asset.checkState === state) && (!language || asset.language === language || asset.language === "zxx");
}

export function validateDecision(value: ReviewDecision): ReviewState | null {
  if (!value || !["check", "request", "approve", "override", "send-back"].includes(value.kind)) return { status: "error", message: "Choose a review action." };
  if (value.kind === "override" || value.kind === "send-back") {
    const reason = typeof value.reason === "string" ? value.reason.trim().replace(/\s+/gu, " ") : "";
    const min = value.kind === "override" ? 3 : 2;
    if (reason.length < min || reason.length > 500) return { status: "error", fieldErrors: { reason: [`Explain the decision in ${min} to 500 characters.`] } };
  }
  if (value.kind === "send-back" && (!Array.isArray(value.assetIds) || value.assetIds.length < 1 || value.assetIds.length > 50 || value.assetIds.some(id => typeof id !== "string" || !/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(id)) || new Set(value.assetIds).size !== value.assetIds.length)) return { status: "error", message: "Choose 1 to 50 different assets to send back." };
  return null;
}
