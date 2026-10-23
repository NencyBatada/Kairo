import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import CalendarPage from "./page";
import EditPage from "./[id]/page";
import NewPage from "./new/page";
import { event, regions } from "./fixtures.test-data";

const { getWorkspace, coreApi, unwrap, get, notFound } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), get: vi.fn(), notFound: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("next/navigation", () => ({ notFound }));
vi.mock("./actions", () => ({ saveFestival: vi.fn(), removeFestival: vi.fn() }));
beforeEach(() => {
  vi.clearAllMocks(); getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["admin"] });
  coreApi.mockResolvedValue({ GET: get }); unwrap.mockImplementation(async (result) => result.data);
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/options") ? { regions } : [event, { ...event, id: "event-2", name: "Karnataka day", regions: [regions[1]] }] }));
  notFound.mockImplementation(() => { throw new Error("not-found"); });
});
const params = Promise.resolve({ workspace: "acme", id: "event-1" });
test("loads date ranges in the workspace and includes national events in a market", async () => {
  render(await CalendarPage({ params, searchParams: Promise.resolve({ market: "IN-MH", from: "2026-11-01", to: "2026-11-30" }) }));
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/triggers/festivals", { params: { path: { brandId: "brand-1" }, query: { from: "2026-11-01", to: "2026-11-30" } } });
  expect(screen.getByRole("heading", { name: "November 2026" })).toBeTruthy();
  expect(screen.getByRole("heading", { name: "Diwali" })).toBeTruthy(); expect(screen.queryByText("Karnataka day")).toBeNull();
  expect(screen.getByRole("link", { name: "Edit event: Diwali" }).getAttribute("href")).toBe("/acme/triggers/festivals/event-1");
});
test("invalid date queries show an actionable message without calling the calendar API", async () => {
  render(await CalendarPage({ params, searchParams: Promise.resolve({ from: "2026-02-30" }) }));
  expect(screen.getByText(/Choose valid dates/)).toBeTruthy(); expect(get).toHaveBeenCalledTimes(1);
});
test("members see events without editing controls", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  render(await CalendarPage({ params, searchParams: Promise.resolve({}) }));
  expect(screen.queryByRole("link", { name: "Add event" })).toBeNull(); expect(screen.queryByRole("link", { name: /Edit event: Diwali/ })).toBeNull();
});
test("direct editing and creation require an admin", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  const view = render(await EditPage({ params })); expect(screen.getByText(/Only workspace admins/)).toBeTruthy(); view.unmount();
  render(await NewPage({ params })); expect(screen.getByText(/Only workspace admins/)).toBeTruthy(); expect(get).not.toHaveBeenCalled();
});
test("missing or removed events show not found, and old dates remain editable", async () => {
  await expect(EditPage({ params: Promise.resolve({ workspace: "acme", id: "gone" }) })).rejects.toThrow("not-found");
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/triggers/festivals", { params: { path: { brandId: "brand-1" }, query: { from: "0001-01-01" } } });
});
