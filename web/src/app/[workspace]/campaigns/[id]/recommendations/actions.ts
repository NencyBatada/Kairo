"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { validId, validVersion, validateDecision, type CareState, type Decision } from "./_lib/model";

export async function decideRecommendation(brandId: string, recommendationId: string, version: number, decision: Decision): Promise<CareState> {
  if (!validId(brandId) || !validId(recommendationId) || !validVersion(version)) return { status: "error", message: "Reload the recommendation before deciding." };
  const invalid = validateDecision(decision);
  if (invalid) return invalid;
  let result;
  try {
    const api = await coreApi(), params = { path: { brandId, recommendationId } };
    result = decision.kind === "approve"
      ? await api.POST("/api/brands/{brandId}/recommendations/{recommendationId}/approve", { params, body: { version } })
      : await api.POST("/api/brands/{brandId}/recommendations/{recommendationId}/dismiss", { params, body: { version, reason: decision.reason.trim() || null } });
  } catch { return { status: "error", conflict: true, message: "Kairo couldn't confirm the outcome. Your reason is still here. Reload recommendations and check Google Ads before deciding again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status === 409 || decision.kind === "approve" && result.response.status >= 500 };
  refresh();
  return { status: "saved", recommendation: result.data, message: decision.kind === "approve" ? "Recommendation approved and applied in Google Ads." : "Recommendation dismissed. The change was not applied." };
}

export async function endTest(brandId: string, campaignId: string, version: number): Promise<CareState> {
  if (!validId(brandId) || !validId(campaignId) || !validVersion(version)) return { status: "error", message: "Reload the campaign before ending its test." };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/end-test", { params: { path: { brandId, campaignId } }, body: { version } }); }
  catch { return { status: "error", conflict: true, message: "Kairo couldn't confirm whether the test ended. Reload the campaign before trying again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status === 409 };
  refresh();
  return { status: "saved", room: result.data, message: "Test ended early. Ads keep running on their current schedule. Kairo will prepare recommendations at the next scheduled check." };
}
