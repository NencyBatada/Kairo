import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { ApprovalsForm } from "./approvals-form";

const settings = {
  briefApprovers: ["u1"],
  assetApprovers: ["u1"],
  launchApprovers: ["u1"],
  budgetOwners: ["u1"],
  budgetLimit: null,
  currency: "INR",
};

const approvers = [
  { userId: "u1", name: "Neha Rao" },
  { userId: "u2", name: "Arjun Mehta" },
];

test("sends each duty's people and the budget limit", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.getAll("assetApprovers")).toEqual(["u1", "u2"]);
    expect(formData.getAll("briefApprovers")).toEqual(["u1"]);
    expect(formData.get("budgetLimit")).toBe("200000");
    return { status: "saved", message: "Approval settings saved." };
  });
  render(<ApprovalsForm action={action} settings={settings} approvers={approvers} canEdit />);

  const assets = screen.getByRole("group", { name: "Asset approvers" });
  fireEvent.click(within(assets).getByLabelText("Arjun Mehta"));
  fireEvent.change(screen.getByLabelText("Per-campaign budget limit (INR)"), { target: { value: "200000" } });
  fireEvent.click(screen.getByRole("button", { name: "Save approval settings" }));

  expect((await screen.findByRole("status")).textContent).toBe("Approval settings saved.");
});

test("explains an empty budget limit", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit />);

  expect(screen.getByText("Leave it empty and every brief and launch goes to a budget owner.")).toBeTruthy();
});

test("is read-only for members who are not admins", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit={false} />);

  expect(screen.queryByRole("button", { name: "Save approval settings" })).toBeNull();
  expect(screen.getByLabelText("Per-campaign budget limit (INR)").matches(":disabled")).toBe(true);
});
