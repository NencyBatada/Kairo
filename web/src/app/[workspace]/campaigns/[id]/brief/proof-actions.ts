"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, type FormState } from "@/lib/forms";
import { version } from "./_lib/model";

export async function chooseHoldouts(brandId: string, campaignId: string, form: FormData): Promise<FormState> {
  const current = version(form.get("version"));
  const regionIds = [...new Set(form.getAll("regionIds").filter((value): value is string => typeof value === "string" && !!value.trim()).map(value => value.trim()))];
  if (current === null) return { status: "error", message: "Load the latest campaign before choosing holdouts." };
  if (!regionIds.length || regionIds.length > 10) return { status: "error", fieldErrors: { regionIds: ["Choose 1 to 10 holdout regions."] } };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/proof-plan/holdouts", { params: { path: { brandId, campaignId } }, body: { version: current, regionIds } }); }
  catch { return { status: "error", message: "Kairo couldn't save the holdouts. Your choices are still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Holdouts sent for recalculation. The map and numbers show the saved plan until the new plan is ready." };
}

export async function changeLockedPlan(brandId: string, campaignId: string, form: FormData): Promise<FormState> {
  const current = version(form.get("version"));
  const reason = String(form.get("reason") ?? "").trim();
  const endDate = String(form.get("endDate") ?? "").trim();
  const ids = [...new Set(form.getAll("regionIds").filter((value): value is string => typeof value === "string" && !!value.trim()).map(value => value.trim()))];
  const errors: Record<string, string[]> = {};
  if (current === null) return { status: "error", message: "Load the latest campaign before changing the test plan." };
  if (reason.length < 2 || reason.length > 500) errors.reason = ["Explain the change in 2 to 500 characters."];
  if (!ids.length || ids.length > 10) errors.regionIds = ["Choose 1 to 10 holdout regions."];
  if (endDate) {
    try { if (!/^\d{4}-\d{2}-\d{2}$/.test(endDate) || Number(endDate.slice(0, 4)) < 1 || new Date(`${endDate}T00:00:00Z`).toISOString().slice(0, 10) !== endDate) errors.endDate = ["Choose a valid end date."]; }
    catch { errors.endDate = ["Choose a valid end date."]; }
  }
  if (Object.keys(errors).length) return { status: "error", fieldErrors: errors };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/proof-plan/changes", { params: { path: { brandId, campaignId } }, body: { version: current, reason, holdoutRegionIds: ids, endDate: endDate || null } }); }
  catch { return { status: "error", message: "Kairo couldn't record the change. Your reason and choices are still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Test plan changed. Your reason is recorded and this version is flagged as changed after launch." };
}
