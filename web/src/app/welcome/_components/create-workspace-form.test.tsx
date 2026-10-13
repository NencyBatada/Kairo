import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { CreateWorkspaceForm } from "./create-workspace-form";

test("sends the form fields to the action", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(Object.fromEntries(formData)).toEqual({ name: "Acme Shoes", handle: "acme", timeZone: "Asia/Kolkata", currency: "INR" });
    return { status: "saved" };
  });
  render(<CreateWorkspaceForm action={action} timeZones={["Asia/Kolkata", "Europe/London"]} currencies={["INR", "USD"]} />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Shoes" } });
  fireEvent.change(screen.getByLabelText("Handle"), { target: { value: "acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

  await vi.waitFor(() => expect(action).toHaveBeenCalledOnce());
});

test("shows field errors from the core next to their fields", async () => {
  const action = vi.fn(async (): Promise<FormState> => ({ status: "error", fieldErrors: { handle: ["That handle is taken."] } }));
  render(<CreateWorkspaceForm action={action} timeZones={["Asia/Kolkata"]} currencies={["INR"]} />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Shoes" } });
  fireEvent.change(screen.getByLabelText("Handle"), { target: { value: "acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

  expect(await screen.findByText("That handle is taken.")).toBeTruthy();
});
