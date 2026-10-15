import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { NotificationLink } from "@/components/notification-link";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

beforeEach(() => {
  vi.clearAllMocks();
});

test("marks an unread notification read before opening its screen", async () => {
  const order: string[] = [];
  const markRead = vi.fn(async () => {
    order.push("read");
    return { ok: true };
  });
  push.mockImplementation(() => order.push("open"));
  render(
    <NotificationLink href="/acme/settings/connections" unread markRead={markRead}>
      Google Ads needs reconnecting
    </NotificationLink>,
  );

  fireEvent.click(screen.getByRole("link", { name: "Google Ads needs reconnecting" }));

  await waitFor(() => expect(push).toHaveBeenCalledWith("/acme/settings/connections"));
  expect(markRead).toHaveBeenCalledOnce();
  expect(order).toEqual(["read", "open"]);
});

test("still opens the screen when marking it read fails", async () => {
  const markRead = vi.fn(async () => {
    throw new Error("offline");
  });
  render(
    <NotificationLink href="/acme/today" unread markRead={markRead}>
      Update
    </NotificationLink>,
  );

  fireEvent.click(screen.getByRole("link", { name: "Update" }));

  await waitFor(() => expect(push).toHaveBeenCalledWith("/acme/today"));
});

test("leaves read notifications and new-tab clicks to the browser", () => {
  const markRead = vi.fn(async () => ({ ok: true }));
  const { rerender } = render(
    <NotificationLink href="/acme/today" unread={false} markRead={markRead}>
      Update
    </NotificationLink>,
  );

  fireEvent.click(screen.getByRole("link", { name: "Update" }));
  rerender(
    <NotificationLink href="/acme/today" unread markRead={markRead}>
      Update
    </NotificationLink>,
  );
  fireEvent.click(screen.getByRole("link", { name: "Update" }), { ctrlKey: true });

  expect(markRead).not.toHaveBeenCalled();
  expect(push).not.toHaveBeenCalled();
});
