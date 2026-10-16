import { expect, test } from "vitest";
import { asset, brandId, campaignId } from "../fixtures.test-data";
import { assetName, dateTime, languageName, matchingImage, mediaUrl, safeUrl } from "./model";

test("media only uses the exact authenticated asset version path", () => {
  const path = `/api/brands/${brandId}/campaigns/${campaignId}/assets/a/file?version=2`;
  expect(mediaUrl(path, brandId, campaignId, "a", 2)).toBe(path);
  expect(mediaUrl(path, brandId, "other", "a", 2)).toBeNull();
  expect(mediaUrl("https://foreign.example/video", brandId, campaignId, "a", 2)).toBeNull();
  expect(mediaUrl(path, brandId, campaignId, "a", 3)).toBeNull();
});
test.each(["javascript:alert(1)", "data:image/svg+xml,test", "//example.com", "https://user:pass@example.com", "http://example.com"])("rejects unsafe source %s", (value) => { expect(safeUrl(value)).toBeNull(); });
test("formats recorded names, dates and shared languages", () => {
  expect(safeUrl("https://example.com/photo")).toBe("https://example.com/photo");
  expect(languageName("zxx")).toBe("Shared across languages"); expect(languageName("hi")).toBe("Hindi");
  expect(assetName(asset())).toBe("Search ad · Everyday shoes");
  expect(dateTime("2026-10-16T06:00:00Z", "Asia/Kolkata")).toContain("11:30");
  expect(dateTime("invalid", "UTC")).toBe("Date unavailable");
});
test("matches media by product, placement and language without using unrelated shopping products", () => {
  const title = asset({ kind: "shopping_title", campaignType: "shopping" });
  const image = asset({ kind: "image", campaignType: "shopping", language: "zxx" });
  expect(matchingImage(title, [image])).toBe(image);
  expect(matchingImage(title, [{ ...image, productIds: ["other"] }])).toBeUndefined();
  expect(matchingImage(title, [{ ...image, language: "hi" }])).toBeUndefined();
  expect(matchingImage(title, [{ ...image, campaignType: "demand_gen" }])).toBeUndefined();
});
