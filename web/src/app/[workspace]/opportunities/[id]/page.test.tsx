import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { detail } from "./fixtures.test-data";
import OpportunityPage from "./page";

const { getWorkspace, coreApi, unwrap, get } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), get: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("./actions", () => ({ acceptOpportunity: vi.fn(), saveOpportunity: vi.fn(), unsaveOpportunity: vi.fn(), dismissOpportunity: vi.fn() }));
let item: typeof detail;
beforeEach(() => {
  vi.clearAllMocks();
  item = { ...detail };
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/{opportunityId}") ? item : { timeZone: "Asia/Kolkata" } }));
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => result.data);
});
const page = () => OpportunityPage({ params: Promise.resolve({ workspace: "acme", id: "opp-1" }) });

test("shows workspace-scoped evidence, all growth comparisons, regional demand and weighted score factors", async () => {
  render(await page());
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities/{opportunityId}", { params: { path: { brandId: "brand-1", opportunityId: "opp-1" } } });
  expect(screen.getByRole("heading", { level: 1, name: detail.topic })).toBeTruthy();
  expect(screen.getByText("+20.0%")).toBeTruthy();
  expect(screen.getByText("Not available")).toBeTruthy();
  expect(screen.getByText("Maharashtra")).toBeTruthy();
  expect(screen.getByText("42 / 100 · 40% weight")).toBeTruthy();
  expect(screen.getByText("Feedback adjustment ×0.80")).toBeTruthy();
  expect(screen.getByText(/Last seen.*6:30/i)).toBeTruthy();
  expect(screen.getByRole("link", { name: /Weather report/ }).getAttribute("rel")).toContain("noopener");
  expect(screen.queryByText(/Source status:/)).toBeNull();
});

test("warns about unavailable sources and a short history", async () => {
  item = { ...item, reasons: [], reasonsStatus: "unavailable", trend: [] };
  render(await page());
  expect(screen.getByText(/No source currently supports/)).toBeTruthy();
  expect(screen.getByText(/Less than 12 months/)).toBeTruthy();
});

test("does not render unsafe source URLs and marks unsupported reasons", async () => {
  item = { ...item, reasons: [{ text: "An unverified explanation.", sources: [{ title: "Unsafe", url: "javascript:alert(1)" }] }], reasonsStatus: "unsourced" };
  render(await page());
  expect(screen.queryByRole("link", { name: /Unsafe/ })).toBeNull();
  expect(screen.getByText("No source supports this reason.")).toBeTruthy();
});

test("limits actions to marketers and links accepted opportunities to their room", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["admin"] });
  const { unmount } = render(await page());
  expect(screen.queryByRole("button", { name: "Accept opportunity" })).toBeNull();
  expect(screen.getByText(/Only workspace marketers/)).toBeTruthy();
  unmount();
  item = { ...item, status: "accepted", roomId: "room-1" };
  render(await page());
  expect(screen.getByRole("link", { name: "Open campaign room" }).getAttribute("href")).toBe("/acme/campaigns/room-1");
});
