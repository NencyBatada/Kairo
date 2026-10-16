"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { readEdit, tidy, version, type AssetState } from "./_lib/edit";

function failure(error: unknown, status: number): AssetState {
  const state = problemToFormState(error, status);
  return { ...state, message: state.message ?? "Check the highlighted fields and try again.", conflict: status === 409 };
}

export async function saveAsset(brandId: string, campaignId: string, assetId: string, kind: string, form: FormData): Promise<AssetState> {
  const { body, errors } = readEdit(kind, form);
  if (Object.keys(errors).length) return { status: "error", message: "Check the highlighted fields.", fieldErrors: errors };
  let result;
  try { result = await (await coreApi()).PATCH("/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}", { params: { path: { brandId, campaignId, assetId } }, body }); }
  catch { return { status: "error", message: "The asset could not be saved. Your edits are still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Asset saved. Its checks need to run again.", asset: result.data };
}

export async function setAssetLock(brandId: string, campaignId: string, assetId: string, assetVersion: number | string, locked: boolean): Promise<AssetState> {
  const current = version(assetVersion);
  if (current === null || typeof locked !== "boolean") return { status: "error", message: "Reload the asset before changing its lock." };
  let result;
  try {
    const api = await coreApi();
    const options = { params: { path: { brandId, campaignId, assetId } }, body: { version: current } };
    result = locked ? await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/lock", options) : await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/unlock", options);
  } catch { return { status: "error", message: "The lock could not be changed. Try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  refresh();
  return { status: "saved", message: locked ? "Asset locked. Kairo will keep this version." : "Asset unlocked. It can be edited or regenerated again.", asset: result.data };
}

export async function regenerateAsset(brandId: string, campaignId: string, assetId: string, assetVersion: number | string, instruction: string): Promise<AssetState> {
  const current = version(assetVersion);
  const cleaned = typeof instruction === "string" ? tidy(instruction) : "";
  if (current === null) return { status: "error", message: "Reload the asset before regenerating it." };
  if (cleaned.length < 2 || cleaned.length > 500) return { status: "error", message: "Check the instruction.", fieldErrors: { instruction: ["Describe the change in 2 to 500 characters."] } };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/regenerate", { params: { path: { brandId, campaignId, assetId } }, body: { version: current, instruction: cleaned } }); }
  catch { return { status: "error", message: "Regeneration could not be started. Your instruction is still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Regeneration queued. The saved version stays visible until the new version is ready.", runId: result.data?.runId };
}
