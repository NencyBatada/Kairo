import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { NotificationBell } from "@/components/notification-bell";

test("links to the notification list and announces the unread count in words", () => {
  render(<NotificationBell href="/acme/settings/notifications" unreadCount={3} />);

  const bell = screen.getByRole("link", { name: "Notifications, 3 unread" });
  expect(bell.getAttribute("href")).toBe("/acme/settings/notifications");
  expect(bell.textContent).toBe("3");
});

test("shows no count when everything is read", () => {
  render(<NotificationBell href="/acme/settings/notifications" unreadCount={0} />);

  expect(screen.getByRole("link", { name: "Notifications, none unread" }).textContent).toBe("");
});

test("says one unread in the singular and caps a long count", () => {
  const { rerender } = render(<NotificationBell href="/n" unreadCount={1} />);
  expect(screen.getByRole("link", { name: "Notifications, 1 unread" })).toBeTruthy();

  rerender(<NotificationBell href="/n" unreadCount={250} />);
  expect(screen.getByRole("link", { name: "Notifications, 250 unread" }).textContent).toBe("99+");
});
