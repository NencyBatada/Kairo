import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import type { components } from "@/lib/core-api/schema";

import { campaign, ownerId } from "./fixtures.test-data";
import Page from "./page";
import ErrorPage from "./error";

const { getWorkspace, coreApi, get, unwrap } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), get: vi.fn(), unwrap: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
let list: components["schemas"]["CampaignPage"];
const page = (search = {}) => Page({ params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve(search) });
beforeEach(() => {
  vi.clearAllMocks(); list = { campaigns: [campaign], nextBefore: "639276282000000000" };
  getWorkspace.mockResolvedValue({ id: "brand-1" }); coreApi.mockResolvedValue({ GET: get }); unwrap.mockImplementation(async (result) => result.data);
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/campaigns") ? list : path.endsWith("/members") ? [{ userId: ownerId, name: "Priya Shah" }] : { timeZone: "Asia/Kolkata" } }));
});

test("loads scoped real campaigns with every filter and local dates", async () => {
  render(await page({ q: "Rain Runner", stage: "brief_draft", status: "waiting_approval", owner: ownerId, from: "2026-10-01", to: "2026-10-31" }));
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/campaigns", { params: { path: { brandId: "brand-1" }, query: { Q: "Rain Runner", Stage: "brief_draft", Status: "waiting_approval", Owner: ownerId, From: "2026-10-01", To: "2026-10-31", Before: undefined } } });
  expect(screen.getByRole("link", { name: campaign.title }).getAttribute("href")).toBe("/acme/campaigns/room-1");
  expect(screen.getByText("Rain Runner")).toBeTruthy(); expect(screen.getByText("Gate 1 · Brief waiting")).toBeTruthy();
  expect(screen.getByText(/15 Oct 2026, 9:00/i)).toBeTruthy();
  expect((screen.getByLabelText("Owner") as HTMLSelectElement).value).toBe(ownerId);
  expect((screen.getByLabelText("Search campaigns or products") as HTMLInputElement).value).toBe("Rain Runner");
});

test("older and latest links retain search and status, while forms restart paging", async () => {
  render(await page({ q: "rain & sun", status: "in_progress", before: "639276282000000001" }));
  const older = new URL(screen.getByRole("link", { name: "Older campaigns" }).getAttribute("href")!, "http://localhost");
  expect(older.searchParams.get("q")).toBe("rain & sun"); expect(older.searchParams.get("status")).toBe("in_progress"); expect(older.searchParams.get("before")).toBe(list.nextBefore);
  expect(screen.getByRole("link", { name: "Latest campaigns" }).getAttribute("href")).not.toContain("before=");
  const form = screen.getByRole("form", { name: "Filter campaigns" }); expect(form.getAttribute("method")).toBe("get"); expect(form.querySelector('[name="before"]')).toBeNull();
});

test("navigation back to unfiltered results resets all filter controls", async () => {
  const view = render(await page({ q: "trainers", stage: "live", status: "agent_working", owner: ownerId, from: "2026-10-01" }));
  view.rerender(await page());
  for (const label of ["Search campaigns or products", "Stage", "Status", "Owner", "Opened from", "Opened through"]) expect((screen.getByLabelText(label) as HTMLInputElement).value).toBe("");
  expect(screen.queryByRole("link", { name: "Clear filters" })).toBeNull();
});

test.each([{ from: "2026-02-30" }, { stage: "invalid" }, { owner: "not-a-guid" }, { before: "9999999999999999999999" }, { q: ["first", "second"] }])("invalid filters show actionable errors without querying campaigns: %j", async (query) => {
  render(await page(query));
  expect(screen.getByText("Check the campaign filters.")).toBeTruthy();
  expect(get.mock.calls.some(([path]) => path.endsWith("/campaigns"))).toBe(false);
});

test("keeps invalid values and associates errors with controls", async () => {
  render(await page({ q: "x".repeat(101), from: "2026-10-20", to: "2026-10-10" }));
  const search = screen.getByLabelText("Search campaigns or products") as HTMLInputElement;
  expect(search.value).toHaveLength(101); expect(search.getAttribute("aria-invalid")).toBe("true");
  expect(search.getAttribute("aria-describedby")).toBe("campaign-q-error");
  expect((screen.getByLabelText("Opened through") as HTMLInputElement).value).toBe("2026-10-10");
});

test("shows first-use, filtered-empty and exhausted-page states", async () => {
  list = { campaigns: [], nextBefore: null };
  const view = render(await page()); expect(screen.getByText("No campaigns yet")).toBeTruthy(); expect(screen.getByRole("link", { name: "Explore opportunities" }).getAttribute("href")).toBe("/acme/opportunities");
  view.rerender(await page({ q: "none" })); expect(screen.getByText("No campaigns match these filters")).toBeTruthy();
  view.rerender(await page({ before: "639276282000000000" })); expect(screen.getByText("No older campaigns")).toBeTruthy(); expect(screen.queryByRole("link", { name: "Older campaigns" })).toBeNull();
});

test("retains former members as owner filter options and handles missing products", async () => {
  const formerId = "ab100000-0000-0000-0000-000000000002";
  list = { campaigns: [{ ...campaign, products: [], owner: { id: formerId, name: "Former member" }, status: "needs_attention", pendingGate: null }], nextBefore: null };
  render(await page({ owner: formerId }));
  expect((screen.getByLabelText("Owner") as HTMLSelectElement).value).toBe(formerId);
  expect(screen.getByText("No product names are available.")).toBeTruthy(); expect(screen.getByRole("link", { name: campaign.title }).textContent).toContain("Needs attention");
});

test("passes API failures to the retry boundary", async () => {
  get.mockRejectedValue(new Error("unavailable")); await expect(page()).rejects.toThrow("unavailable");
  const reset = vi.fn(); render(<ErrorPage reset={reset} />); fireEvent.click(screen.getByRole("button", { name: "Try again" })); expect(reset).toHaveBeenCalledOnce();
});
