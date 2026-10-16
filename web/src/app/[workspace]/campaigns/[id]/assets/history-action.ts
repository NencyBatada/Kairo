"use server";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import type { AssetHistory } from "./_lib/model";

export async function loadAssetHistory(brandId: string, campaignId: string, assetId: string): Promise<{ history?: AssetHistory; error?: string }> {
  let result;
  try { result = await (await coreApi()).GET("/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/history", { params: { path: { brandId, campaignId, assetId } } }); }
  catch { return { error: "Asset history could not be loaded. Try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { error: result.response.status === 404 ? "This asset is no longer available." : "Asset history could not be loaded. Try again." };
  return { history: result.data };
}
