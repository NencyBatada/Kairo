import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { activity, room } from "./fixtures.test-data";
import Page from "./page";
import Layout from "./layout";

const { getRoom, coreApi, get, unwrap } = vi.hoisted(() => ({ getRoom: vi.fn(), coreApi: vi.fn(), get: vi.fn(), unwrap: vi.fn() }));
vi.mock("./_lib/room", () => ({ getRoom }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("./actions", () => ({ retryRun: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/acme/campaigns/room-1" }));
const params = Promise.resolve({ workspace: "acme", id: "room-1" });
const page = (query = {}) => Page({ params, searchParams: Promise.resolve(query) });

beforeEach(() => {
  vi.clearAllMocks(); getRoom.mockResolvedValue({ room, brandId: "brand-1", timeZone: "Asia/Kolkata" });
  coreApi.mockResolvedValue({ GET: get }); get.mockResolvedValue({ data: activity }); unwrap.mockImplementation(async (result) => result.data);
});

test("loads workspace-scoped activity with actor, exact local time and paging", async () => {
  render(await page());
  expect(getRoom).toHaveBeenCalledWith("acme", "room-1");
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/campaigns/{campaignId}/activity", { params: { path: { brandId: "brand-1", campaignId: "room-1" }, query: { before: undefined } } });
  expect(screen.getByText("Agent run failed")).toBeTruthy();
  expect(screen.getByText("Plan agent · Agent")).toBeTruthy();
  expect(screen.getByRole("region", { name: "Activity log" }).textContent).toMatch(/9:02/i);
  expect(screen.getByRole("link", { name: "Older activity" }).getAttribute("href")).toBe("/acme/campaigns/room-1?before=51");
});

test("supports older activity and returning to latest", async () => {
  get.mockResolvedValue({ data: { entries: [], nextBefore: null } });
  render(await page({ before: "51" }));
  expect(get.mock.calls[0][1].params.query).toEqual({ before: "51" });
  expect(screen.getByText("No older activity")).toBeTruthy();
  expect(screen.getByRole("link", { name: "Latest activity" }).getAttribute("href")).toBe("/acme/campaigns/room-1");
  expect(screen.queryByRole("link", { name: "Older activity" })).toBeNull();
});

test.each(["bad", "0", "9223372036854775808", ["1", "2"]])("invalid activity cursors fall back with an explanation: %s", async (before) => {
  render(await page({ before }));
  expect(screen.getByText(/activity page link is invalid/)).toBeTruthy();
  expect(get.mock.calls[0][1].params.query.before).toBeUndefined();
});

test("shows an honest empty activity state", async () => {
  get.mockResolvedValue({ data: { entries: [], nextBefore: null } });
  render(await page()); expect(screen.getByText("No activity yet")).toBeTruthy();
});

test("room layout shares its tracker and retains a way back to overview", async () => {
  render(await Layout({ params, children: <p>Stage content</p> }));
  expect(screen.getByRole("heading", { level: 1, name: room.title })).toBeTruthy();
  expect(screen.getByRole("region", { name: "Campaign progress" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Overview" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("link", { name: "All campaigns" }).getAttribute("href")).toBe("/acme/campaigns");
});

test("passes missing-room and API failures to the route boundaries", async () => {
  getRoom.mockRejectedValue(new Error("not-found"));
  await expect(page()).rejects.toThrow("not-found");
  await expect(Layout({ params, children: null })).rejects.toThrow("not-found");
});
