"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { integer, readSettings, type SettingsState } from "./_lib/settings";

export async function saveCheckSettings(brandId: string, settingsVersion: number | string, input: unknown): Promise<SettingsState> {
  const version = integer(settingsVersion, 2147483647);
  if (version === null) return { status: "error", message: "Reload the check settings before saving." };
  let current;
  try { current = await (await coreApi()).GET("/api/brands/{brandId}/check-settings", { params: { path: { brandId } } }); }
  catch { return { status: "error", message: "The check settings could not be reached. Your edits are still here; try again." }; }
  if (current.response.status === 401) return signInAgain();
  if (!current.response.ok) return problemToFormState(current.error, current.response.status);
  if (Number(current.data!.version) !== version) return { status: "error", message: "Someone else changed the check settings. Your edits are kept; reload the settings and review the changes before saving.", conflict: true };
  const { body, errors } = readSettings(input, current.data!);
  if (Object.keys(errors).length) return { status: "error", message: "Check the highlighted fields.", fieldErrors: errors };
  let result;
  try { result = await (await coreApi()).PUT("/api/brands/{brandId}/check-settings", { params: { path: { brandId } }, body: { version, ...body } }); }
  catch { return { status: "error", message: "The check settings could not be saved. Your edits are still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status === 409 };
  refresh();
  return { status: "saved", message: "Check settings saved. Existing asset checks keep their original results; run checks again to use these settings.", settings: result.data };
}
