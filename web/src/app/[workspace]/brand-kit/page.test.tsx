import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import BrandKitPage from "./page";

const { getWorkspace, coreApi, get, signInAgain, unwrap } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), get: vi.fn(), signInAgain: vi.fn(), unwrap: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain, unwrap }));
vi.mock("./actions", () => ({ saveKit: vi.fn(), approveKit: vi.fn(), discardKit: vi.fn(), removeKitFile: vi.fn(), draftKit: vi.fn(), restoreKit: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/acme/brand-kit", useRouter: () => ({ refresh: vi.fn(), replace: vi.fn() }) }));

beforeEach(() => {
  getWorkspace.mockResolvedValue({ id: "brand-1", handle: "acme", roles: ["admin"] });
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => { if (!result.response.ok) throw new Error("API error"); return result.data; });
  get.mockImplementation(async (path: string) => ({ response: { status: path.endsWith("latest") ? 204 : 200, ok: true }, data: path.endsWith("latest") ? undefined : path.endsWith("/brand-kit") ? { approved: null, draft: null } : path === "/api/brands/{brandId}" ? { timeZone: "Asia/Kolkata" } : [] }));
});

test("loads Brand Kit data for the resolved workspace and handles no drafting run", async () => {
  render(await BrandKitPage({ params: Promise.resolve({ workspace: "acme" }) }));
  expect(getWorkspace).toHaveBeenCalledWith("acme");
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/brand-kit", { params: { path: { brandId: "brand-1" } } });
  expect(screen.getByRole("heading", { name: "Create your Brand Kit" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Save draft" })).toBeTruthy();
});

test("members can view an empty kit without admin actions", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", handle: "acme", roles: ["marketer"] });
  render(await BrandKitPage({ params: Promise.resolve({ workspace: "acme" }) }));
  expect(screen.queryByRole("button", { name: "Save draft" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Draft from website" })).toBeNull();
});

test("does not present a failed drafting lookup as an empty successful state", async () => {
  const initial = get.getMockImplementation()!;
  get.mockImplementation(async (...args: unknown[]) => String(args[0]).endsWith("latest") ? { response: { status: 500, ok: false } } : initial(...args));
  await expect(BrandKitPage({ params: Promise.resolve({ workspace: "acme" }) })).rejects.toThrow("API error");
});

test("local edits survive another admin discarding the draft during a live refresh", async () => {
  const initial = get.getMockImplementation()!;
  const draft = { number: 2, revision: 4, status: "draft", content: { voice: { summary: "Friendly" } }, files: [] };
  let kit = { approved: null, draft: draft as typeof draft | null };
  get.mockImplementation(async (...args: unknown[]) => String(args[0]).endsWith("/brand-kit") ? { response: { ok: true }, data: kit } : initial(...args));
  const { rerender } = render(await BrandKitPage({ params: Promise.resolve({ workspace: "acme" }) }));
  fireEvent.change(screen.getByLabelText("Voice summary"), { target: { value: "Keep this local edit" } });
  kit = { approved: null, draft: null };
  rerender(await BrandKitPage({ params: Promise.resolve({ workspace: "acme" }) }));
  expect((screen.getByLabelText("Voice summary") as HTMLTextAreaElement).value).toBe("Keep this local edit");
  expect(screen.getByText(/changed elsewhere/)).toBeTruthy();
});
