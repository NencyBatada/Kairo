"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { batchContext, validId, validVersion, type StockDecision, type StockState } from "./_lib/model";

export async function changeStock(brandId: string, campaignId: string, decision: StockDecision): Promise<StockState> {
  if (!validId(brandId) || !validId(campaignId) || !decision || !validId(decision.id) || !["undo", "confirm", "dismiss"].includes(decision.kind)) return { status: "error", message: "Reload the pause log before deciding." };
  if (decision.kind === "undo" ? !validVersion(decision.version) : typeof decision.context !== "string" || !decision.context) return { status: "error", message: "Reload the pause log before deciding." };
  const api = await coreApi();
  if (decision.kind !== "undo") {
    let current;
    try { current = await api.GET("/api/brands/{brandId}/campaigns/{campaignId}/stock-pauses", { params: { path: { brandId, campaignId } } }); }
    catch { return { status: "error", message: "Kairo couldn't check the held pauses. Try again." }; }
    if (current.response.status === 401) return signInAgain();
    if (!current.response.ok || !current.data) return { ...problemToFormState(current.error, current.response.status), status: "error", message: "Kairo couldn't check the held pauses. Reload the log and try again." };
    if (!current.data.pauses.some(pause => pause.batchId === decision.id && pause.state === "held") || batchContext(current.data, decision.id) !== decision.context) return { status: "error", conflict: true, message: "The stock feed or held pauses changed. Reload and review the batch again." };
  }
  let result;
  try {
    switch (decision.kind) {
      case "undo": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/stock-pauses/{pauseId}/undo", { params: { path: { brandId, campaignId, pauseId: decision.id } }, body: { version: decision.version } }); break;
      case "confirm": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/stock-pauses/batches/{batchId}/confirm", { params: { path: { brandId, campaignId, batchId: decision.id } } }); break;
      case "dismiss": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/stock-pauses/batches/{batchId}/dismiss", { params: { path: { brandId, campaignId, batchId: decision.id } } }); break;
    }
  } catch { return { status: "error", message: "Google Ads couldn't confirm the change. Reload the log before trying again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status === 409 };
  refresh();
  return { status: "saved", log: result.data, message: { undo: "Pause undone. The ad is enabled in Google Ads and can spend again.", confirm: "Held pauses confirmed. The listed ads are paused in Google Ads.", dismiss: "Held pauses dismissed. The listed ads stay enabled." }[decision.kind] };
}
