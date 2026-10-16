"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { version } from "../assets/_lib/edit";
import { validateDecision, type ReviewDecision, type ReviewState } from "./_lib/review";

export async function reviewAssets(brandId: string, campaignId: string, campaignVersion: number | string, decision: ReviewDecision): Promise<ReviewState> {
  const current = version(campaignVersion);
  if (current === null) return { status: "error", message: "Reload the campaign before deciding." };
  const invalid = validateDecision(decision);
  if (invalid) return invalid;
  if (decision.kind === "override" && (version(decision.assetVersion) === null || typeof decision.assetId !== "string" || !/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(decision.assetId))) return { status: "error", message: "Reload the asset before overriding it." };
  let result;
  try {
    const api = await coreApi();
    const params = { path: { brandId, campaignId } };
    switch (decision.kind) {
      case "check": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/assets/check", { params, body: { version: current } }); break;
      case "request": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/approval-requests", { params, body: { version: current, gate: "assets" } }); break;
      case "approve": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/approve", { params, body: { version: current, gate: "assets", comment: null } }); break;
      case "override": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/override", { params: { path: { brandId, campaignId, assetId: decision.assetId } }, body: { version: version(decision.assetVersion), reason: decision.reason.trim().replace(/\s+/gu, " ") } }); break;
      case "send-back": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/assets/send-back", { params, body: { version: current, assetIds: decision.assetIds, comment: decision.reason.trim().replace(/\s+/gu, " ") } }); break;
    }
  } catch { return { status: "error", message: "Kairo couldn't record the decision. Your comment is still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) {
    const state = problemToFormState(result.error, result.response.status);
    return { ...state, fieldErrors: state.fieldErrors?.comment ? { ...state.fieldErrors, reason: state.fieldErrors.comment } : state.fieldErrors, message: state.message ?? "Check the highlighted fields and try again.", conflict: result.response.status === 409 };
  }
  refresh();
  return { status: "saved", message: { check: "Checks queued. Saved results stay visible until the agents finish.", request: "Assets sent for Gate 2 approval.", approve: "Assets approved. The campaign is ready for launch approval.", override: "Flag overridden. Your reason is recorded with this asset version.", "send-back": "Selected assets sent back. Unlocked assets will be regenerated with your comment; locked assets are kept." }[decision.kind] };
}
