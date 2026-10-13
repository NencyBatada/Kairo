import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { MemberRow } from "./member-row";

const priya = {
  userId: "u2",
  name: "Priya Shah",
  email: "priya@example.com",
  pictureUrl: null,
  roles: ["marketer" as const],
  joinedAt: "2026-10-13T00:00:00Z",
};

test("lets an admin change a member's roles", async () => {
  const changeRoles = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.getAll("roles")).toEqual(["marketer", "approver"]);
    return { status: "saved", message: "Roles saved." };
  });
  render(<MemberRow member={priya} isSelf={false} canManage changeRoles={changeRoles} remove={vi.fn()} />);

  fireEvent.click(screen.getByLabelText("Approver"));
  fireEvent.click(screen.getByRole("button", { name: "Save roles" }));

  expect((await screen.findByRole("status")).textContent).toBe("Roles saved.");
});

test("shows the reason when leaving is refused", async () => {
  const remove = vi.fn(async (): Promise<FormState> => ({ status: "error", message: "A workspace needs at least one admin." }));
  render(<MemberRow member={priya} isSelf canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Leave workspace" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Leave this workspace?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Leave workspace" }));

  expect((await screen.findByRole("alert")).textContent).toBe("A workspace needs at least one admin.");
});

test("asks before removing someone and does nothing if cancelled", async () => {
  const remove = vi.fn();
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(remove).not.toHaveBeenCalled();
});

test("removes someone once confirmed", async () => {
  const remove = vi.fn(async (): Promise<FormState> => ({ status: "saved", message: "Removed." }));
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove" }));

  await waitFor(() => expect(remove).toHaveBeenCalled());
});

test("shows roles as text to members who are not admins", () => {
  render(<MemberRow member={priya} isSelf={false} canManage={false} remove={vi.fn()} changeRoles={vi.fn()} />);

  expect(screen.getByText("Marketer")).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
});

test("keeps long names and emails whole in a title", () => {
  const long = { ...priya, name: "Priyadarshini Venkataraghavan Subramanian-Iyer", email: "priyadarshini.venkataraghavan@acme-footwear.example" };
  render(<MemberRow member={long} isSelf={false} canManage={false} remove={vi.fn()} changeRoles={vi.fn()} />);

  expect(screen.getByText(long.name).getAttribute("title")).toBe(long.name);
  expect(screen.getByText(long.email).getAttribute("title")).toBe(long.email);
});

test("blocks a second removal while the first is still running", async () => {
  let finish: (state: FormState) => void = () => {};
  const remove = vi.fn(() => new Promise<FormState>((resolve) => (finish = resolve)));
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove" }));
  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());

  await waitFor(() => expect(screen.getByRole("button", { name: "Remove" }).matches(":disabled")).toBe(true));
  finish({ status: "saved", message: "Removed." });
  await waitFor(() => expect(screen.getByRole("button", { name: "Remove" }).matches(":disabled")).toBe(false));
});
