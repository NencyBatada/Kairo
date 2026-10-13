import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { InvitationList } from "./invitation-list";

const invitation = {
  id: "inv-1",
  workspaceName: "Acme Shoes",
  workspaceHandle: "acme",
  roles: ["marketer" as const, "approver" as const],
  invitedBy: "Neha Rao",
  expiresAt: "2026-10-27T00:00:00Z",
};

test("says who invited you and with which roles", () => {
  render(<InvitationList invitations={[invitation]} accept={vi.fn()} decline={vi.fn()} />);

  expect(screen.getByText("Acme Shoes")).toBeTruthy();
  expect(screen.getByText("Neha Rao invited you as Marketer, Approver.")).toBeTruthy();
});

test("accepts or declines the chosen invitation", async () => {
  const accept = vi.fn(async () => {});
  const decline = vi.fn(async () => {});
  render(<InvitationList invitations={[invitation]} accept={accept} decline={decline} />);

  fireEvent.click(screen.getByRole("button", { name: "Accept" }));
  fireEvent.click(screen.getByRole("button", { name: "Decline" }));

  await vi.waitFor(() => expect(accept).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
  await vi.waitFor(() => expect(decline).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
});
