import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import { detail, draftRoom, proof } from "./fixtures.test-data";
import Page from "./page";

const { getRoom, getWorkspace, get } = vi.hoisted(() => ({ getRoom: vi.fn(), getWorkspace: vi.fn(), get: vi.fn() }));
vi.mock("../_lib/room", () => ({ getRoom }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: async () => ({ GET: get }), unwrap: async (result: { data: unknown }) => result.data }));
vi.mock("./actions", () => ({ saveBrief: vi.fn(), proposeBrief: vi.fn(), applyBrief: vi.fn(), requestBriefApproval: vi.fn(), decideBrief: vi.fn() }));
const params = Promise.resolve({ workspace: "acme", id: "room-1" });
beforeEach(() => { vi.clearAllMocks(); getRoom.mockResolvedValue({ room: draftRoom, brandId: "brand-1", timeZone: "Asia/Kolkata", now: 0 }); getWorkspace.mockResolvedValue({ roles: ["marketer"] }); get.mockImplementation(async (path: string) => ({ data: path.endsWith("/brief") ? detail : proof })); });
test("loads brief and test plan in workspace scope and respects marketer role", async () => {
  render(await Page({ params, searchParams: Promise.resolve({}) })); expect(get).toHaveBeenCalledTimes(2); expect(get.mock.calls[0][1].params.path).toEqual({ brandId: "brand-1", campaignId: "room-1" }); expect(screen.getByRole("button", { name: "Save brief" })).toBeTruthy(); expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
});
test("no brief is an honest empty state", async () => {
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/brief") ? { ...detail, brief: null } : proof }));
  render(await Page({ params, searchParams: Promise.resolve({}) })); expect(screen.getByRole("heading", { name: "No brief yet" })).toBeTruthy(); expect(screen.queryByRole("button")).toBeNull();
});
test("viewers cannot edit and API failures reach the route boundary", async () => {
  getWorkspace.mockResolvedValue({ roles: ["viewer"] }); render(await Page({ params, searchParams: Promise.resolve({}) })); expect(screen.queryByRole("button", { name: "Save brief" })).toBeNull(); get.mockRejectedValue(new Error("unavailable")); await expect(Page({ params, searchParams: Promise.resolve({}) })).rejects.toThrow("unavailable");
});
