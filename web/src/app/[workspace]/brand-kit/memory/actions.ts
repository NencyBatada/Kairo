"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { validId, validVersion, validateEdit, type MemoryState } from "./_lib/model";

export async function editMemory(brandId: string, entryId: string, version: number, title: string, lesson: string): Promise<MemoryState> {
  if (!validId(brandId) || !validId(entryId) || !validVersion(version) || typeof title !== "string" || typeof lesson !== "string") return { status: "error", message: "Reload the entry before editing." };
  const invalid = validateEdit(title, lesson);
  if (invalid) return invalid;
  let result;
  try { result = await (await coreApi()).PATCH("/api/brands/{brandId}/brand-memory/{entryId}", { params: { path: { brandId, entryId } }, body: { version, title: title.trim(), lesson: lesson.trim() } }); }
  catch { return { status: "error", conflict: true, message: "Kairo couldn't confirm the save. Your draft is still here. Reload the entry and review its current version before saving again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status === 409 || result.response.status >= 500 };
  refresh();
  return { status: "saved", detail: result.data, message: "Memory entry saved." };
}

export async function removeMemory(brandId: string, entryId: string, version: number): Promise<MemoryState> {
  if (!validId(brandId) || !validId(entryId) || !validVersion(version)) return { status: "error", message: "Reload the entry before removing it." };
  let current, result;
  try { current = await (await coreApi()).GET("/api/brands/{brandId}/brand-memory/{entryId}", { params: { path: { brandId, entryId } } }); }
  catch { return { status: "error", message: "The entry could not be checked. Try again when your connection is restored." }; }
  if (current.response.status === 401) return signInAgain();
  if (!current.response.ok) return problemToFormState(current.error, current.response.status);
  if (!current.data || Number(current.data.entry.version) !== version || current.data.entry.state !== "active") return { status: "error", conflict: true, message: "This entry changed or was removed. Cancel, reload and review it before removing it." };
  try { result = await (await coreApi()).DELETE("/api/brands/{brandId}/brand-memory/{entryId}", { params: { path: { brandId, entryId } } }); }
  catch { return { status: "error", conflict: true, message: "Kairo couldn't confirm removal. Reload the entry before trying again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status >= 500 };
  refresh();
  return { status: "saved", removed: true, message: "Memory entry removed. Future agent work will not use it." };
}
