import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import VersionPage from "./page";

const { getWorkspace, get, coreApi, unwrap, notFound } = vi.hoisted(() => ({ getWorkspace: vi.fn(), get: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), notFound: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("next/navigation", () => ({ notFound }));
vi.mock("../../actions", () => ({ saveKit: vi.fn(), approveKit: vi.fn(), discardKit: vi.fn(), restoreKit: vi.fn() }));
const oldVersion = { number: 1, status: "superseded", revision: 1, content: { voice: { summary: "Old voice" } }, files: [], origin: "manual" };
beforeEach(() => {
  vi.clearAllMocks();
  getWorkspace.mockResolvedValue({ id: "b", roles: ["admin"] });
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => result.data);
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/brand-kit") ? { draft: { revision: 8 }, approved: null } : oldVersion }));
  notFound.mockImplementation(() => { throw new Error("not found"); });
});
const props = { params: Promise.resolve({ workspace: "acme", number: "1" }) };
test("shows a historical version read-only with restoration for admins", async () => {
  render(await VersionPage(props));
  expect((screen.getByLabelText("Voice summary") as HTMLTextAreaElement).value).toBe("Old voice");
  expect(screen.getByRole("button", { name: "Restore into draft" })).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Save draft" })).toBeNull();
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/brand-kit/versions/{number}", { params: { path: { brandId: "b", number: 1 } } });
});
test("does not restore an approved version or expose restoration to members", async () => {
  getWorkspace.mockResolvedValue({ id: "b", roles: ["marketer"] });
  render(await VersionPage(props));
  expect(screen.queryByRole("button", { name: "Restore into draft" })).toBeNull();
});
test("rejects invalid version numbers before calling the API", async () => {
  await expect(VersionPage({ params: Promise.resolve({ workspace: "acme", number: "1.5" }) })).rejects.toThrow("not found");
  expect(get).not.toHaveBeenCalled();
});
