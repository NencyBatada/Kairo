import type { components } from "@/lib/core-api/schema";

export type AlternativeView = components["schemas"]["AlternativeView"];
export type AlternativeContent = components["schemas"]["AlternativeContent"];

export function accessibilityUrl(value: string | null | undefined): string | null {
  if (!value || !/^\/api\/brands\/[0-9a-f-]{36}\/media-accessibility\?/i.test(value)) return null;
  const url = new URL(value, "https://kairo.invalid");
  if (url.origin !== "https://kairo.invalid" || url.hash || url.searchParams.has("file")) return null;
  return url.pathname + url.search;
}

export function alternativeFileUrl(base: string, value: string | null | undefined, kind: "captions" | "described", revision: number | string, hash: string) {
  return value === `${base}&file=${kind}&revision=${revision}&sourceHash=${encodeURIComponent(hash)}` ? value : undefined;
}

export async function mediaRequest(url: string, method = "GET", body?: unknown): Promise<AlternativeView> {
  const response = await fetch(url, { method, cache: "no-store", headers: { "X-Kairo-Request": "1", ...(body === undefined ? {} : { "Content-Type": "application/json" }) }, body: body === undefined ? undefined : JSON.stringify(body) });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.title ?? "The media alternatives are unavailable. Try again.");
  return data as AlternativeView;
}
