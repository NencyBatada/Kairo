import "server-only";

import { cache } from "react";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

export const getRoom = cache(async (handle: string, campaignId: string) => {
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const [room, brand] = await Promise.all([
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}", { params: { path: { brandId: workspace.id, campaignId } } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }).then(unwrap),
  ]);
  return { room, timeZone: brand.timeZone, brandId: workspace.id, now: Date.now() };
});
