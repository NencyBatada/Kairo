import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { NotificationInbox } from "./notification-inbox";

vi.mock("../actions", () => ({
  markNotificationRead: vi.fn(),
  markAllNotificationsRead: vi.fn(),
}));

const item = {
  id: 9,
  kind: "connection.problem",
  title: "Google Ads needs reconnecting",
  body: "Reconnect Google Ads in Settings.",
  link: "/settings/connections",
  createdAt: "2026-10-14T09:30:00Z",
  readAt: null,
};

test("lists notifications with links and an unread count", () => {
  render(
    <NotificationInbox
      brandId="brand-1"
      workspace="acme"
      timeZone="Asia/Kolkata"
      olderHref={() => "/acme/settings/notifications?before=1"}
      page={{ items: [item], unreadCount: 1, nextBefore: null }}
    />,
  );

  expect(screen.getByRole("status").textContent).toBe("1 unread notification");
  expect(screen.getByRole("link", { name: new RegExp(item.title) }).getAttribute("href")).toBe("/acme/settings/connections");
  expect(screen.getByText("New")).toBeTruthy();
});

test("shows an empty state when there are no notifications", () => {
  render(
    <NotificationInbox
      brandId="brand-1"
      workspace="acme"
      timeZone="UTC"
      olderHref={() => "/acme/settings/notifications"}
      page={{ items: [], unreadCount: 0, nextBefore: null }}
    />,
  );

  expect(screen.getByRole("heading", { name: "Nothing to act on" })).toBeTruthy();
});
