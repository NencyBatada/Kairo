import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { WorkspaceNotifications } from "./workspace-notifications";

const { getWorkspace, getUnreadNotificationCount } = vi.hoisted(() => ({
  getWorkspace: vi.fn(),
  getUnreadNotificationCount: vi.fn(),
}));

vi.mock("@/lib/core-api/queries", () => ({ getWorkspace, getUnreadNotificationCount }));

test("renders the server-side unread count for the current workspace", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", handle: "acme" });
  getUnreadNotificationCount.mockResolvedValue(4);

  render(await WorkspaceNotifications({ params: Promise.resolve({ workspace: "acme" }) }));

  expect(getUnreadNotificationCount).toHaveBeenCalledWith("brand-1");
  expect(screen.getByRole("link", { name: "Notifications, 4 unread" }).getAttribute("href")).toBe("/acme/settings/notifications");
});
