import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { NotificationInbox } from "./notification-inbox";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
vi.mock("../actions", () => ({
  markNotificationRead: vi.fn(async () => ({ ok: true })),
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
      page={{ items: [], unreadCount: 0, nextBefore: null }}
    />,
  );

  expect(screen.getByRole("heading", { name: "Nothing to act on" })).toBeTruthy();
});

test("marks a notification read when it is opened from the list", async () => {
  const { markNotificationRead } = await import("../actions");
  render(
    <NotificationInbox
      brandId="brand-1"
      workspace="acme"
      timeZone="Asia/Kolkata"
      page={{ items: [item], unreadCount: 1, nextBefore: null }}
    />,
  );

  fireEvent.click(screen.getByRole("link", { name: new RegExp(item.title) }));

  await waitFor(() => expect(markNotificationRead).toHaveBeenCalledWith("brand-1", 9));
  await waitFor(() => expect(push).toHaveBeenCalledWith("/acme/settings/connections"));
});

test("links to older notifications when there are more", () => {
  render(
    <NotificationInbox
      brandId="brand-1"
      workspace="acme"
      timeZone="UTC"
      page={{ items: [item], unreadCount: 1, nextBefore: 5 }}
    />,
  );

  expect(screen.getByRole("link", { name: "Older notifications" }).getAttribute("href")).toBe("/acme/settings/notifications?before=5");
});
