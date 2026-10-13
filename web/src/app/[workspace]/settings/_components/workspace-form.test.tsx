import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { WorkspaceForm } from "./workspace-form";

const workspace = {
  id: "b1",
  handle: "acme",
  name: "Acme Shoes",
  timeZone: "Asia/Kolkata",
  currency: "INR",
  roles: ["admin" as const],
};

test("lets an admin rename the workspace", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.get("name")).toBe("Acme Footwear");
    return { status: "saved", message: "Saved." };
  });
  render(<WorkspaceForm action={action} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Footwear" } });
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect((await screen.findByRole("status")).textContent).toBe("Saved.");
});

test("shows the settings read-only to everyone else", () => {
  render(<WorkspaceForm action={vi.fn()} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit={false} />);

  expect(screen.getByLabelText("Brand name").matches(":disabled")).toBe(true);
  expect(screen.queryByRole("button", { name: "Save changes" })).toBeNull();
  expect(screen.getByText("INR")).toBeTruthy();
});

test("keeps the saved time zone selected even when it is not in the offered list", () => {
  render(<WorkspaceForm action={vi.fn()} workspace={workspace} timeZones={["Europe/London"]} canEdit />);

  expect((screen.getByLabelText("Time zone") as HTMLSelectElement).value).toBe("Asia/Kolkata");
});

test("explains that the handle and currency can't be changed", () => {
  render(<WorkspaceForm action={vi.fn()} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit />);

  expect(screen.getByText("The handle and currency can't be changed after a workspace is created.")).toBeTruthy();
});

test("tells members who aren't admins why they can't edit", () => {
  render(<WorkspaceForm action={vi.fn()} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit={false} />);

  expect(screen.getByText("Only admins can change these details.")).toBeTruthy();
});
