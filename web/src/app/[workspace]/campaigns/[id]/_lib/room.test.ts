import { beforeEach, expect, test, vi } from "vitest";

import { room } from "../fixtures.test-data";
import { getRoom } from "./room";

const { getWorkspace, coreApi, get, unwrap } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), get: vi.fn(), unwrap: vi.fn() }));
vi.mock("server-only", () => ({}));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
beforeEach(() => {
  vi.clearAllMocks(); getWorkspace.mockResolvedValue({ id: "brand-1" }); coreApi.mockResolvedValue({ GET: get }); unwrap.mockImplementation(async (result) => { if (result.error) throw new Error(result.error); return result.data; });
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/{campaignId}") ? room : { timeZone: "Asia/Kolkata" } }));
});

test("resolves the workspace and loads the scoped room and timezone", async () => {
  expect(await getRoom("acme", "room-1")).toMatchObject({ room, brandId: "brand-1", timeZone: "Asia/Kolkata", now: expect.any(Number) });
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/campaigns/{campaignId}", { params: { path: { brandId: "brand-1", campaignId: "room-1" } } });
});

test("propagates access and missing-room failures through the shared API handling", async () => {
  get.mockResolvedValue({ error: "not-found" }); await expect(getRoom("acme", "gone")).rejects.toThrow("not-found");
  getWorkspace.mockRejectedValue(new Error("not-a-member")); await expect(getRoom("other", "room-1")).rejects.toThrow("not-a-member");
});
