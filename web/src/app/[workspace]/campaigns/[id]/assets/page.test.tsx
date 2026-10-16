import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import { asset, assetSet, brandId, campaignId } from "./fixtures.test-data";
import Page from "./page";
const { getRoom, get } = vi.hoisted(() => ({ getRoom: vi.fn(), get: vi.fn() }));
vi.mock("../_lib/room", () => ({ getRoom }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: async () => ({ GET: get }), unwrap: async (result: { data: unknown }) => result.data }));
vi.mock("./history-action", () => ({ loadAssetHistory: vi.fn() }));
const params = Promise.resolve({ workspace: "acme", id: campaignId });
beforeEach(() => { vi.clearAllMocks(); getRoom.mockResolvedValue({ brandId, timeZone: "Asia/Kolkata" }); get.mockResolvedValue({ data: assetSet([asset(), asset({ id: "hi", language: "hi" }), asset({ id: "shared", language: "zxx", kind: "image" })]) }); });
test("scopes reads and preserves shared assets when filtering language", async () => {
  render(await Page({ params, searchParams: Promise.resolve({ language: "hi", type: "search" }) }));
  expect(getRoom).toHaveBeenCalledWith("acme", campaignId); expect(get.mock.calls[0][1].params.path).toEqual({ brandId, campaignId });
  expect(screen.queryByRole("heading", { name: "Search · English" })).toBeNull(); expect(screen.getByRole("heading", { name: "Search · Hindi" })).toBeTruthy(); expect(screen.getByRole("heading", { name: "Search · Shared across languages" })).toBeTruthy();
});
test("invalid and repeated filters fall back to all assets", async () => {
  render(await Page({ params, searchParams: Promise.resolve({ language: ["en", "hi"], type: "toString" }) }));
  expect(screen.getByRole("heading", { name: "Search · English" })).toBeTruthy(); expect(screen.getByRole("link", { name: "All types" }).getAttribute("aria-current")).toBe("page");
});
test("network errors reach the route error boundary", async () => { get.mockRejectedValue(new Error("offline")); await expect(Page({ params, searchParams: Promise.resolve({}) })).rejects.toThrow("offline"); });
