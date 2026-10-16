import type { components } from "@/lib/core-api/schema";

export type Region = components["schemas"]["ProofRegionView"];
export type MapRegion = Region & { kind: "test" | "holdout"; number: number };

export function coordinates(region: Region) {
  if (region.latitude == null || region.longitude == null) return null;
  const latitude = Number(region.latitude), longitude = Number(region.longitude);
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude) || Math.abs(latitude) > 90 || Math.abs(longitude) > 180) return null;
  return { x: longitude + 180, y: 90 - latitude };
}

export function bounds(regions: Region[]) {
  const points = regions.map(coordinates).filter(point => point !== null);
  if (!points.length) return { x: 0, y: 0, width: 360, height: 180 };
  const xs = points.map(point => point.x), ys = points.map(point => point.y);
  const left = Math.min(...xs), right = Math.max(...xs), top = Math.min(...ys), bottom = Math.max(...ys);
  let width = Math.min(360, Math.max(24, (right - left) * 1.5));
  let height = Math.min(180, Math.max(14, (bottom - top) * 1.5));
  if (width / height < 1.7) width = Math.min(360, height * 1.7);
  else height = Math.min(180, width / 1.7);
  return { x: Math.max(0, Math.min(360 - width, (left + right - width) / 2)), y: Math.max(0, Math.min(180 - height, (top + bottom - height) / 2)), width, height };
}

export function ranked(regions: Region[]) {
  return [...new Map(regions.map(region => [region.id, region])).values()].sort((a, b) => {
    const score = (value: Region) => value.matchScore == null || !Number.isFinite(Number(value.matchScore)) ? -Infinity : Number(value.matchScore);
    return score(b) - score(a) || a.name.localeCompare(b.name);
  });
}
