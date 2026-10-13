import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { PendingInvitations } from "./pending-invitations";

const invitation = {
  id: "inv-1",
  email: "priya@example.com",
  roles: ["marketer" as const],
  invitedBy: "Neha Rao",
  sentAt: "2026-10-13T00:00:00Z",
  expiresAt: "2026-10-27T00:00:00Z",
};

test("revokes the chosen invitation", async () => {
  const revoke = vi.fn(async () => {});
  render(<PendingInvitations invitations={[invitation]} revoke={revoke} />);

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));

  await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
});

test("says so when nobody is waiting", () => {
  render(<PendingInvitations invitations={[]} revoke={vi.fn()} />);

  expect(screen.getByText("No invitations are waiting.")).toBeTruthy();
});
