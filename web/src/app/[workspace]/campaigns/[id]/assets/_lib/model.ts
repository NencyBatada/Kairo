import type { components } from "@/lib/core-api/schema";

export type Asset = components["schemas"]["AssetView"];
export type AssetSet = components["schemas"]["AssetSetView"];
export type AssetHistory = components["schemas"]["AssetHistoryView"];
export type AssetContent = components["schemas"]["AssetContentView"];

export const campaignTypes = { search: "Search", shopping: "Shopping", demand_gen: "Demand Gen" };
export const kindNames: Record<string, string> = { keyword_set: "Keywords", search_ad: "Search ad", demand_gen_text: "Demand Gen text", shopping_title: "Shopping title", image: "Image", video: "Video", voiceover: "Voiceover", footage: "Footage" };

export function languageName(code: string) {
  if (code === "zxx") return "Shared across languages";
  try { return new Intl.DisplayNames(["en"], { type: "language" }).of(code) ?? code; }
  catch { return code; }
}

export function safeUrl(value: string | null | undefined) {
  if (!value) return null;
  try { const url = new URL(value); return url.protocol === "https:" && !url.username && !url.password ? url.href : null; }
  catch { return null; }
}

export function mediaUrl(value: string | null, brandId: string, campaignId: string, assetId: string, version: number | string) {
  const expected = `/api/brands/${brandId}/campaigns/${campaignId}/assets/${assetId}/file?version=${version}`;
  return value === expected ? expected : null;
}

export function assetName(asset: Pick<Asset, "kind" | "adGroup" | "format" | "content">) {
  return [kindNames[asset.kind] ?? asset.kind, asset.adGroup ?? asset.content.offerId ?? asset.format].filter(Boolean).join(" · ");
}

export function dateTime(value: string, timeZone: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Date unavailable" : new Intl.DateTimeFormat("en-IN", { timeZone, dateStyle: "medium", timeStyle: "short" }).format(date);
}

export function matchingImage(asset: Asset, assets: Asset[]) {
  return assets.find((candidate) => candidate.kind === "image" && candidate.campaignType === asset.campaignType && (candidate.language === asset.language || candidate.language === "zxx") && asset.productIds.some((id) => candidate.productIds.includes(id)))
    ?? (asset.kind === "demand_gen_text" ? assets.find((candidate) => candidate.kind === "image" && candidate.campaignType === asset.campaignType && (candidate.language === asset.language || candidate.language === "zxx")) : undefined);
}
