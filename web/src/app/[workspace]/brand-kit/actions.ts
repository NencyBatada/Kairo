"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text } from "@/lib/forms";

import type { KitActionState, KitContent, KitVersion } from "./_components/types";

export async function saveKit(brandId: string, _: KitActionState, data: FormData): Promise<KitActionState> {
  const revision = revisionOf(data);
  if (revision === null) return stale();
  let content: KitContent;
  try {
    content = JSON.parse(text(data, "content"));
    if (!content || typeof content !== "object" || Array.isArray(content)) return invalidContent();
  } catch {
    return invalidContent();
  }
  return perform(async () => (await coreApi()).PUT("/api/brands/{brandId}/brand-kit/draft", {
    params: { path: { brandId } }, body: { content, revision },
  }), "Draft saved. Review it before approving.", true);
}

export async function approveKit(brandId: string, _: KitActionState, data: FormData): Promise<KitActionState> {
  const revision = revisionOf(data);
  if (revision === null) return stale();
  return perform(async () => (await coreApi()).POST("/api/brands/{brandId}/brand-kit/draft/approve", {
    params: { path: { brandId } }, body: { revision },
  }), "Brand Kit approved. Agents will use this version.");
}

export async function restoreKit(brandId: string, number: number, _: KitActionState, data: FormData): Promise<KitActionState> {
  const revision = revisionOf(data);
  if (revision === null) return stale();
  return perform(async () => (await coreApi()).POST("/api/brands/{brandId}/brand-kit/versions/{number}/restore", {
    params: { path: { brandId, number } }, body: { revision },
  }), "Version restored into the draft. Review and approve it before agents use it.");
}

export async function discardKit(brandId: string, _: KitActionState, _data: FormData): Promise<KitActionState> {
  void [_, _data];
  return perform(async () => (await coreApi()).DELETE("/api/brands/{brandId}/brand-kit/draft", {
    params: { path: { brandId } },
  }), "Draft discarded.");
}

export async function removeKitFile(brandId: string, fileId: string, _: KitActionState, _data: FormData): Promise<KitActionState> {
  void [_, _data];
  return perform(async () => (await coreApi()).DELETE("/api/brands/{brandId}/brand-kit/files/{fileId}", {
    params: { path: { brandId, fileId } },
  }), "File removed from available files and the draft. Older versions keep their copy.");
}

export async function draftKit(brandId: string, _: KitActionState, data: FormData): Promise<KitActionState> {
  return perform(async () => (await coreApi()).POST("/api/brands/{brandId}/brand-kit/drafting", {
    params: { path: { brandId } },
    body: { websiteUrl: text(data, "websiteUrl"), replaceDraft: data.get("replaceDraft") === "on" },
  }), "Drafting started. The draft will appear here when it is ready.");
}

type Result = { data?: unknown; error?: unknown; response: { ok: boolean; status: number } };

async function perform(call: () => Promise<Result>, message: string, withVersion = false): Promise<KitActionState> {
  let result: Result;
  try {
    result = await call();
  } catch {
    return { status: "error", message: "Kairo couldn't reach the service. Please try again." };
  }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  refresh();
  return { status: "saved", message, ...(withVersion ? { version: result.data as KitVersion } : {}) };
}

function revisionOf(data: FormData) {
  const value = text(data, "revision");
  const revision = Number(value);
  return value !== "" && Number.isSafeInteger(revision) && revision >= 0 ? revision : null;
}

function stale(): KitActionState {
  return { status: "error", message: "Reload the Brand Kit to review its latest version before trying again." };
}

function invalidContent(): KitActionState {
  return { status: "error", message: "The draft could not be read. Copy your edits, then reload and try again." };
}
