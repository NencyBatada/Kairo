import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { ApprovalsForm } from "./approvals-form";

const settings = {
  briefApprovers: ["u1"],
  assetApprovers: ["u1"],
  launchApprovers: ["u1"],
  budgetOwners: ["u1"],
  budgetMoveApprovers: ["u1"],
  targetChangeApprovers: ["u2"],
  googleSuggestionApprovers: ["u1", "u2"],
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

test("shows the gates and each type of recommendation in their own groups", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit />);

  const gates = screen.getByRole("region", { name: "Approval gates" });
  const recommendations = screen.getByRole("region", { name: "Recommendations" });
  expect(within(gates).getAllByRole("group").map((group) => group.querySelector("legend")?.textContent)).toEqual([
    "Brief approvers",
    "Asset approvers",
    "Launch approvers",
    "Budget owners",
  ]);
  expect(within(recommendations).getAllByRole("group").map((group) => group.querySelector("legend")?.textContent)).toEqual([
    "Budget moves",
    "Target CPA and ROAS changes",
    "Google Ads recommendations",
  ]);
});

test("ticks the people already approving each type of recommendation", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit />);

  const ticked = (name: string) =>
    within(screen.getByRole("group", { name }))
      .getAllByRole("checkbox")
      .map((box) => (box as HTMLInputElement).checked);
  expect(ticked("Budget moves")).toEqual([true, false]);
  expect(ticked("Target CPA and ROAS changes")).toEqual([false, true]);
  expect(ticked("Google Ads recommendations")).toEqual([true, true]);
});

test("sends the approvers for each type of recommendation", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.getAll("budgetMoveApprovers")).toEqual(["u1", "u2"]);
    expect(formData.getAll("targetChangeApprovers")).toEqual([]);
    expect(formData.getAll("googleSuggestionApprovers")).toEqual(["u1", "u2"]);
    return { status: "error", fieldErrors: { targetChangeApprovers: ["Choose at least one person."] } };
  });
  render(<ApprovalsForm action={action} settings={settings} approvers={approvers} canEdit />);

  fireEvent.click(within(screen.getByRole("group", { name: "Budget moves" })).getByLabelText("Arjun Mehta"));
  fireEvent.click(within(screen.getByRole("group", { name: "Target CPA and ROAS changes" })).getByLabelText("Arjun Mehta"));
  fireEvent.click(screen.getByRole("button", { name: "Save approval settings" }));

  expect(await within(screen.getByRole("group", { name: "Target CPA and ROAS changes" })).findByText("Choose at least one person.")).toBeTruthy();
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

test("tells members who aren't admins why they can't edit", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit={false} />);

  expect(screen.getByText("Only admins can change who approves what.")).toBeTruthy();
});

test("explains an empty list of approvers", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={[]} canEdit />);

  expect(screen.getAllByText("Nobody has the approver role yet. Give someone the role on the Team page.")).toHaveLength(7);
});
