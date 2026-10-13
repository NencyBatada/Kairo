import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";
import { timeZoneOptions } from "@/lib/time-zones";

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

test("marks a select invalid when the core rejects its value", async () => {
  const action = vi.fn(async (): Promise<FormState> => ({ status: "error", fieldErrors: { currency: ["Kairo doesn't support that currency yet."] } }));
  render(<CreateWorkspaceForm action={action} timeZones={["Asia/Kolkata"]} currencies={["INR"]} />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Shoes" } });
  fireEvent.change(screen.getByLabelText("Handle"), { target: { value: "acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

  await screen.findByText("Kairo doesn't support that currency yet.");
  expect(screen.getByLabelText("Currency").getAttribute("aria-invalid")).toBe("true");
  expect(screen.getByLabelText("Time zone").getAttribute("aria-invalid")).toBeNull();
});

function description(element: HTMLElement) {
  return (element.getAttribute("aria-describedby") ?? "")
    .split(" ")
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" ");
}

test("warns that the handle and currency can't be changed later", () => {
  render(<CreateWorkspaceForm action={vi.fn()} timeZones={["Asia/Kolkata"]} currencies={["INR"]} />);

  expect(description(screen.getByLabelText("Handle"))).toContain("You can't change it later.");
  expect(description(screen.getByLabelText("Currency"))).toContain("You can't change it later.");
});

test("starts on India's time zone when every zone is offered", () => {
  render(<CreateWorkspaceForm action={vi.fn()} timeZones={timeZoneOptions()} currencies={["INR"]} />);

  expect((screen.getByLabelText("Time zone") as HTMLSelectElement).value).toBe("Asia/Kolkata");
});
