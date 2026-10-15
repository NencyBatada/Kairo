import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { opportunity } from "@/components/opportunities/fixtures.test-data";

import OpportunitiesPage from "./page";

const { getWorkspace, coreApi, unwrap, get } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), get: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));

let list: { items: typeof opportunity[]; refreshedAt: string | null; lastOutcome: string | null; lastReason: string | null };
beforeEach(() => {
  vi.clearAllMocks();
  list = { items: [opportunity], refreshedAt: "2026-10-15T01:00:00Z", lastOutcome: "succeeded", lastReason: null };
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/opportunities") ? list : { timeZone: "Asia/Kolkata" } }));
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => result.data);
});
const page = (query = {}) => OpportunitiesPage({ params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve(query) });

test("loads ranked live opportunities with workspace-scoped filters and local refresh time", async () => {
  render(await page({ status: "saved", minScore: "60", category: "Trainers", watched: "true" }));
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities", { params: { path: { brandId: "brand-1" }, query: { status: "saved", category: "Trainers", watched: true, minScore: 60 } } });
  expect(screen.getByRole("heading", { level: 1, name: "Opportunities" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Waterproof trainers" }).getAttribute("href")).toBe("/acme/opportunities/opportunity-1");
  expect(screen.getByText("+42.0%")).toBeTruthy();
  expect(screen.getByText("Watched")).toBeTruthy();
  expect(screen.getByText(/Last refreshed.*6:30/i)).toBeTruthy();
  expect(screen.getByText("Search demand is rising ahead of the rains.")).toBeTruthy();
  expect(screen.getByText(/Maharashtra/)).toBeTruthy();
});

test("renders saved navigation, topic management and submitted filter values", async () => {
  render(await page({ status: "saved", minScore: "60" }));
  expect(screen.getByRole("link", { name: "Saved opportunities" }).getAttribute("href")).toBe("/acme/opportunities?status=saved");
  expect(screen.getByRole("link", { name: "Manage topics" }).getAttribute("href")).toBe("/acme/opportunities/topics");
  expect((screen.getByLabelText("Show") as HTMLSelectElement).value).toBe("saved");
  expect((screen.getByLabelText("Minimum score") as HTMLInputElement).value).toBe("60");
  expect(screen.getByRole("link", { name: "Clear filters" })).toBeTruthy();
});

test("distinguishes first refresh, empty results and filters with no matches", async () => {
  list = { ...list, items: [], refreshedAt: null };
  const { unmount } = render(await page());
  expect(screen.getByText("No opportunities yet")).toBeTruthy();
  expect(screen.getByText(/first opportunity refresh/)).toBeTruthy();
  unmount();
  render(await page({ status: "saved" }));
  expect(screen.getByText("No opportunities match these filters")).toBeTruthy();
});

test("keeps previous opportunities visible after a failed refresh", async () => {
  list.lastOutcome = "failed"; list.lastReason = "google_ads";
  render(await page());
  expect(screen.getByText(/Google Ads could not provide fresh demand data/)).toBeTruthy();
  expect(screen.getByRole("link", { name: "Waterproof trainers" })).toBeTruthy();
});

test("shows honest missing evidence and trend fallbacks", async () => {
  list.items = [{ ...opportunity, reason: null, trend: [], regions: [], products: [], productCount: 0, watched: false }];
  render(await page());
  expect(screen.getByText(/No supported reason/)).toBeTruthy();
  expect(screen.getByText(/No rising regions reported/)).toBeTruthy();
  expect(screen.queryByText("Watched")).toBeNull();
});

test("resets filter controls when navigating back to the unfiltered route", async () => {
  const { rerender } = render(await page({ status: "saved", minScore: "60", watched: "true" }));
  rerender(await page());
  expect((screen.getByLabelText("Show") as HTMLSelectElement).value).toBe("open");
  expect((screen.getByLabelText("Topics") as HTMLSelectElement).value).toBe("");
  expect((screen.getByLabelText("Minimum score") as HTMLInputElement).value).toBe("");
});
