"use server";

import { refresh } from "next/cache";

import { signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text } from "@/lib/forms";

import { catalogueRequest } from "./_lib/api";
import { importOutcome } from "./_lib/catalogue";
import type { CatalogueActionState, CatalogueImport } from "./_lib/types";

export async function setFeed(brandId: string, _: CatalogueActionState, data: FormData): Promise<CatalogueActionState> {
  return mutate(brandId, "/feed", { method: "PUT", body: JSON.stringify({ url: text(data, "url") }) }, "Feed saved.", true);
}
export async function refreshFeed(brandId: string, ..._form: [CatalogueActionState, FormData]): Promise<CatalogueActionState> {
  void _form;
  return mutate(brandId, "/feed/refresh", { method: "POST" }, "Feed refreshed.", true);
}
export async function stopFeed(brandId: string, ..._form: [CatalogueActionState, FormData]): Promise<CatalogueActionState> {
  void _form;
  return mutate(brandId, "/feed", { method: "DELETE" }, "Feed refresh stopped. Products are still available.");
}
export async function fixProduct(brandId: string, productId: string, _: CatalogueActionState, data: FormData): Promise<CatalogueActionState> {
  const body: Record<string, string | number | null> = {};
  for (const field of ["title", "price", "link", "imageLink", "category"] as const) {
    if (data.get(`clear:${field}`) === "on") { body[field] = null; continue; }
    const value = text(data, field);
    if (!value) continue;
    if (field === "price") {
      const price = Number(value);
      if (!Number.isFinite(price) || price < 0) return { status: "error", fieldErrors: { price: ["Enter a price of zero or more."] } };
      body.price = price; body.currency = text(data, "currency").toUpperCase();
    } else body[field] = value;
  }
  if (!Object.keys(body).length) return { status: "error", message: "Enter a missing detail or choose a fix to clear." };
  return mutate(brandId, `/products/${encodeURIComponent(productId)}/fixes`, { method: "PATCH", body: JSON.stringify(body) }, "Product details saved. Fixes apply while the source leaves these fields empty.");
}

async function mutate(brandId: string, path: string, options: RequestInit, message: string, hasImport = false): Promise<CatalogueActionState> {
  let response: Response;
  try { response = await catalogueRequest(brandId, path, options); }
  catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (response.status === 401) return signInAgain();
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) return problemToFormState(data, response.status);
  if (hasImport) {
    const result = data?.import ?? data;
    if (!result || !["succeeded", "unchanged", "failed", "suspicious"].includes(result.status)) return { status: "error", message: "Kairo couldn't read the import result. Reload the catalogue before trying again." };
    refresh();
    return { ...importOutcome(result), import: result as CatalogueImport };
  }
  refresh();
  return { status: "saved", message };
}
