import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { InviteForm } from "./invite-form";

test("sends the email and the chosen roles", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.get("email")).toBe("priya@example.com");
    expect(formData.getAll("roles")).toEqual(["marketer"]);
    return { status: "saved", message: "Invitation sent." };
  });
  render(<InviteForm action={action} />);

  fireEvent.change(screen.getByLabelText("Email"), { target: { value: "priya@example.com" } });
  fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

  expect((await screen.findByRole("status")).textContent).toBe("Invitation sent.");
});

test("shows why the core refused the invitation", async () => {
  const action = vi.fn(async (): Promise<FormState> => ({ status: "error", fieldErrors: { email: ["That person is already a member."] } }));
  render(<InviteForm action={action} />);

  fireEvent.change(screen.getByLabelText("Email"), { target: { value: "priya@example.com" } });
  fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

  expect(await screen.findByText("That person is already a member.")).toBeTruthy();
});
