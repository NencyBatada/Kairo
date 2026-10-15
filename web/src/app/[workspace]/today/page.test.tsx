import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import TodayPage from "./page";

const { getWorkspace, getNotifications, coreApi, unwrap } = vi.hoisted(() => ({
  getWorkspace: vi.fn(),
  getNotifications: vi.fn(),
  coreApi: vi.fn(),
  unwrap: vi.fn(),
}));

vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("../settings/notifications/actions", () => ({ markNotificationRead: vi.fn() }));

test("loads current workspace notifications for Today", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1" });
  getNotifications.mockImplementation(async (path: string) =>
    path === "/api/brands/{brandId}" ? { data: { timeZone: "Asia/Kolkata" } } : { data: { items: [] } },
  );
  coreApi.mockResolvedValue({ GET: getNotifications });
  unwrap.mockImplementation(async (result) => result.data);

  render(await TodayPage({ params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve({}) }));

  expect(getWorkspace).toHaveBeenCalledWith("acme");
  expect(getNotifications).toHaveBeenCalledWith("/api/brands/{brandId}/notifications", {
    params: { path: { brandId: "brand-1" }, query: { limit: 5 } },
  });
  expect(getNotifications).toHaveBeenCalledWith("/api/brands/{brandId}", { params: { path: { brandId: "brand-1" } } });
  expect(screen.getByRole("heading", { name: "No recent updates" })).toBeTruthy();
});
