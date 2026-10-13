import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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

test("revokes the chosen invitation once confirmed", async () => {
  const revoke = vi.fn(async () => {});
  render(<PendingInvitations invitations={[invitation]} revoke={revoke} />);

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Revoke the invitation to priya@example.com?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Revoke" }));

  await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
});

test("keeps the invitation when cancelled", async () => {
  const revoke = vi.fn(async () => {});
  render(<PendingInvitations invitations={[invitation]} revoke={revoke} />);

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(revoke).not.toHaveBeenCalled();
});

test("says so when nobody is waiting", () => {
  render(<PendingInvitations invitations={[]} revoke={vi.fn()} />);

  expect(screen.getByText("No invitations are waiting.")).toBeTruthy();
});
