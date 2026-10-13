import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

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

afterEach(() => {
  vi.unstubAllGlobals();
});

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

test("shows the reason when a change is refused", async () => {
  const remove = vi.fn(async (): Promise<FormState> => ({ status: "error", message: "A workspace needs at least one admin." }));
  vi.stubGlobal("confirm", () => true);
  render(<MemberRow member={priya} isSelf canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Leave workspace" }));

  expect((await screen.findByRole("alert")).textContent).toBe("A workspace needs at least one admin.");
});

test("asks before removing someone and does nothing if cancelled", () => {
  const remove = vi.fn();
  vi.stubGlobal("confirm", () => false);
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));

  expect(remove).not.toHaveBeenCalled();
});

test("shows roles as text to members who are not admins", () => {
  render(<MemberRow member={priya} isSelf={false} canManage={false} remove={vi.fn()} changeRoles={vi.fn()} />);

  expect(screen.getByText("Marketer")).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
});
