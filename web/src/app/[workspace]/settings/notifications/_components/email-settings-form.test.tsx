import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { EmailSettingsForm } from "./email-settings-form";

test("submits email toggles for kinds the user can change", async () => {
  const action = vi.fn(async () => ({ status: "idle" as const }));
  render(
    <EmailSettingsForm
      brandId="brand-1"
      action={action}
      settings={{
        kinds: [
          {
            kind: "connection.problem",
            name: "Connection or data problem",
            description: "A connected account needs reconnecting.",
            email: true,
            canTurnOff: true,
          },
          {
            kind: "approval.needed",
            name: "Approval needed",
            description: "Something is waiting for your approval.",
            email: true,
            canTurnOff: false,
          },
        ],
      }}
    />,
  );

  const connection = screen.getByText("Connection or data problem").closest("li")!;
  fireEvent.click(within(connection).getByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "Save email settings" }));

  expect(action).toHaveBeenCalledOnce();
  const call = action.mock.calls[0] as unknown as [string, { status: string }, FormData];
  const formData = call[2];
  expect(formData.getAll("kind")).toEqual(["connection.problem", "approval.needed"]);
  expect(formData.get("email-0")).toBeNull();
  expect(formData.get("email-1")).toBe("on");
});
