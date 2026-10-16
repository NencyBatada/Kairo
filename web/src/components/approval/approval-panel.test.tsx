import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { ApprovalPanel, type ApprovalDecision } from "./approval-panel";

const saved: FormState = { status: "saved" };

test("blocked approval still permits an independently enabled send-back with its selected summary", async () => {
  const onDecision = vi.fn(async () => saved);
  renderPanel({ approveDisabled: true, sendBack: { label: "Send selected assets back (1)", confirmLabel: "Send selected assets back", description: "Only selected unlocked assets regenerate.", disabled: false, summary: <p>Selected asset: Walking shoes</p> }, onDecision });
  expect(screen.getByRole("button", { name: "Approve brief" }).matches(":disabled")).toBe(true);
  const sheet = await openSheet("Send selected assets back (1)", "Send selected assets back?");
  expect(within(sheet).getByText("Selected asset: Walking shoes")).toBeTruthy();
  fireEvent.change(within(sheet).getByLabelText("What needs to change"), { target: { value: "Fix the voice" } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Send selected assets back" }));
  await waitFor(() => expect(onDecision).toHaveBeenCalledWith({ kind: "send-back", reason: "Fix the voice" }));
});

test("an open decision keeps its comment and blocks submission after saved work changes", async () => {
  const onDecision = vi.fn(async () => saved);
  const props = { label: "Gate 2", title: "Asset approval", subject: "assets", status: "waiting" as const, approver: "Priya", canDecide: true, onDecision };
  const { rerender } = render(<ApprovalPanel {...props} contextKey="version-1" />);
  const sheet = await openSheet("Send back", "Send the assets back?");
  fireEvent.change(within(sheet).getByLabelText("What needs to change"), { target: { value: "Keep this correction" } });
  rerender(<ApprovalPanel {...props} contextKey="version-2" />);
  expect((within(sheet).getByLabelText("What needs to change") as HTMLTextAreaElement).value).toBe("Keep this correction");
  expect(within(sheet).getByRole("button", { name: "Send back" }).matches(":disabled")).toBe(true);
  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));
  expect(onDecision).not.toHaveBeenCalled();
});

test("a server conflict keeps the reason and requires a fresh review before retrying", async () => {
  const onDecision = vi.fn(async () => ({ status: "error" as const, message: "The saved version changed", conflict: true }));
  renderPanel({ onDecision });
  const sheet = await openSheet("Send back", "Send the brief back?");
  fireEvent.change(within(sheet).getByLabelText("What needs to change"), { target: { value: "Retain this reason" } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));
  expect(await within(sheet).findByText("The saved version changed")).toBeTruthy();
  expect((within(sheet).getByLabelText("What needs to change") as HTMLTextAreaElement).value).toBe("Retain this reason");
  expect(within(sheet).getByRole("button", { name: "Send back" }).matches(":disabled")).toBe(true);
  expect(onDecision).toHaveBeenCalledOnce();
});

function renderPanel(props: Partial<Parameters<typeof ApprovalPanel>[0]> = {}) {
  const onDecision = vi.fn(async (decision: ApprovalDecision): Promise<FormState> => {
    void decision;
    return saved;
  });
  render(
    <ApprovalPanel
      label="Gate 1 · Brief"
      title="Monsoon running shoes"
      subject="brief"
      status="waiting"
      approver="Priya Shah"
      canDecide
      facts={[
        { label: "Budget", value: "₹2,50,000 of ₹2,00,000 limit", tone: "warning", note: "Above the limit, so it goes to the budget owner." },
        { label: "Regions", value: "Maharashtra, Karnataka" },
      ]}
      onDecision={onDecision}
      {...props}
    >
      <p>Summer demand for running shoes in two states.</p>
    </ApprovalPanel>,
  );
  return { onDecision: (props.onDecision as typeof onDecision | undefined) ?? onDecision };
}

async function openSheet(trigger: string, title: string) {
  fireEvent.click(screen.getByRole("button", { name: trigger }));
  return screen.findByRole("dialog", { name: title });
}

test("shows the gate, status, approver, summary and key facts", () => {
  renderPanel();

  const panel = screen.getByRole("region", { name: "Monsoon running shoes" });
  expect(within(panel).getByText("Gate 1 · Brief")).toBeTruthy();
  expect(within(panel).getByText("Waiting for approval")).toBeTruthy();
  expect(within(panel).getByText("Priya Shah")).toBeTruthy();
  expect(within(panel).getByText("Summer demand for running shoes in two states.")).toBeTruthy();
  expect(within(panel).getByText("Budget").tagName).toBe("DT");
  expect(within(panel).getByText("₹2,50,000 of ₹2,00,000 limit").closest("dd")).toBeTruthy();
  expect(within(panel).getByText("Above the limit, so it goes to the budget owner.")).toBeTruthy();
});

test("approving asks for confirmation, then records the decision and announces it", async () => {
  const { onDecision } = renderPanel();

  const sheet = await openSheet("Approve brief", "Approve brief?");
  expect(onDecision).not.toHaveBeenCalled();
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole("button", { name: "Approve brief" })));
  fireEvent.click(within(sheet).getByRole("button", { name: "Approve brief" }));

  await waitFor(() => expect(screen.getByRole("status").textContent).toBe("You approved the brief."));
  expect(onDecision).toHaveBeenCalledWith({ kind: "approve" });
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(screen.getByText("Approved")).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Approve brief" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Send back" })).toBeNull();
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("status")));
});

test("sending back needs a comment before anything is sent", async () => {
  const { onDecision } = renderPanel();

  const sheet = await openSheet("Send back", "Send the brief back?");
  const reason = within(sheet).getByLabelText("What needs to change");
  await waitFor(() => expect(document.activeElement).toBe(reason));

  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));
  expect(onDecision).not.toHaveBeenCalled();
  expect(within(sheet).getByText("Add a comment saying what needs to change.")).toBeTruthy();
  expect(reason.getAttribute("aria-invalid")).toBe("true");
  expect(reason.getAttribute("aria-describedby")).toContain(`${reason.id}-error`);

  fireEvent.change(reason, { target: { value: "   " } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));
  expect(onDecision).not.toHaveBeenCalled();

  fireEvent.change(reason, { target: { value: "  Lead with quick-dry.  " } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));

  await waitFor(() => expect(onDecision).toHaveBeenCalledWith({ kind: "send-back", reason: "Lead with quick-dry." }));
  await waitFor(() => expect(screen.getByRole("status").textContent).toBe("You sent the brief back with your comment."));
  expect(screen.getByText("Sent back")).toBeTruthy();
});

test("overriding a flag needs a reason and keeps the approval open", async () => {
  const { onDecision } = renderPanel({
    label: "Gate 2 · Assets",
    subject: "asset set",
    override: { label: "Override flag", description: "Brand Guardian flagged this asset for a logo mismatch." },
  });

  const sheet = await openSheet("Override flag", "Override flag?");
  expect(within(sheet).getByText("Brand Guardian flagged this asset for a logo mismatch.")).toBeTruthy();
  fireEvent.click(within(sheet).getByRole("button", { name: "Override flag" }));
  expect(within(sheet).getByText("Add a reason for the override.")).toBeTruthy();
  expect(onDecision).not.toHaveBeenCalled();

  fireEvent.change(within(sheet).getByLabelText("Reason for the override"), { target: { value: "Logo is the approved alternate." } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Override flag" }));

  await waitFor(() => expect(onDecision).toHaveBeenCalledWith({ kind: "override", reason: "Logo is the approved alternate." }));
  await waitFor(() => expect(screen.getByRole("status").textContent).toBe("Override recorded with your reason."));
  expect(screen.getByText("Waiting for approval")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Approve asset set" })).toBeTruthy();
});

test("while a decision is pending the confirm button is busy and the sheet stays open", async () => {
  let finish: (state: FormState) => void = () => {};
  const onDecision = vi.fn(() => new Promise<FormState>((resolve) => (finish = resolve)));
  renderPanel({ onDecision });

  const sheet = await openSheet("Approve brief", "Approve brief?");
  fireEvent.click(within(sheet).getByRole("button", { name: "Approve brief" }));

  const confirm = within(sheet).getByRole("button", { name: "Approve brief" });
  await waitFor(() => expect(confirm.getAttribute("aria-busy")).toBe("true"));
  expect(confirm.matches(":disabled")).toBe(true);
  expect(within(sheet).getByRole("button", { name: "Cancel" }).matches(":disabled")).toBe(true);

  fireEvent.keyDown(sheet, { key: "Escape" });
  expect(screen.getByRole("dialog", { name: "Approve brief?" })).toBeTruthy();
  fireEvent.click(confirm);
  expect(onDecision).toHaveBeenCalledTimes(1);

  finish(saved);
  await waitFor(() => expect(screen.getByRole("status").textContent).toBe("You approved the brief."));
});

test("a failed decision shows the cause in the sheet and keeps the comment", async () => {
  const onDecision = vi.fn(async (): Promise<FormState> => ({ status: "error", message: "This brief changed since you opened it. Reload, then decide." }));
  renderPanel({ onDecision });

  const sheet = await openSheet("Send back", "Send the brief back?");
  const reason = within(sheet).getByLabelText("What needs to change");
  fireEvent.change(reason, { target: { value: "Add Hindi." } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));

  expect((await within(sheet).findByRole("alert")).textContent).toBe("This brief changed since you opened it. Reload, then decide.");
  expect((reason as HTMLTextAreaElement).value).toBe("Add Hindi.");
  expect(screen.getByText("Waiting for approval")).toBeTruthy();
  expect(screen.getByRole("status", { hidden: true }).textContent).toBe("");
});

test("a field error from the server sits under the comment", async () => {
  const onDecision = vi.fn(async (): Promise<FormState> => ({ status: "error", fieldErrors: { reason: ["Keep the comment under 2,000 characters."] } }));
  renderPanel({ onDecision });

  const sheet = await openSheet("Send back", "Send the brief back?");
  const reason = within(sheet).getByLabelText("What needs to change");
  fireEvent.change(reason, { target: { value: "Too long" } });
  fireEvent.click(within(sheet).getByRole("button", { name: "Send back" }));

  expect(await within(sheet).findByText("Keep the comment under 2,000 characters.")).toBeTruthy();
  expect(reason.getAttribute("aria-invalid")).toBe("true");
});

test("a decision that throws shows a plain retry message", async () => {
  const onDecision = vi.fn(async (): Promise<FormState> => {
    throw new Error("network");
  });
  renderPanel({ onDecision });

  const sheet = await openSheet("Approve brief", "Approve brief?");
  fireEvent.click(within(sheet).getByRole("button", { name: "Approve brief" }));

  expect((await within(sheet).findByRole("alert")).textContent).toBe("We couldn't record your decision. Check your connection, then try again.");
});

test("cancelling closes the sheet and returns focus to the action", async () => {
  const { onDecision } = renderPanel();

  const trigger = screen.getByRole("button", { name: "Send back" });
  trigger.focus();
  const sheet = await openSheet("Send back", "Send the brief back?");
  fireEvent.click(within(sheet).getByRole("button", { name: "Cancel" }));

  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
  expect(onDecision).not.toHaveBeenCalled();
});

test("people who can't decide see a read-only state naming the approver", () => {
  renderPanel({ canDecide: false });

  expect(screen.queryByRole("button")).toBeNull();
  expect(screen.getByText("Waiting for Priya Shah")).toBeTruthy();
  expect(screen.getByText(/Only the assigned approver can approve or send back this brief/)).toBeTruthy();
});

test("a closed approval offers no actions", () => {
  renderPanel({ status: "expired" });

  expect(screen.getByText("Expired")).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
  expect(screen.queryByText("Waiting for Priya Shah")).toBeNull();
});

test("without an override, send back spans the full phone width under approve", () => {
  renderPanel();

  const bar = document.querySelector<HTMLElement>("[data-slot='approval-actions']")!;
  const [approve, sendBack] = within(bar).getAllByRole("button");
  expect(approve.className.split(" ")).toContain("col-span-2");
  expect(sendBack.className.split(" ")).toContain("col-span-2");
});

test("on phones the actions sit in a sticky bottom bar with safe-area padding and 44px buttons", async () => {
  renderPanel({ override: { label: "Override flag", description: "Flagged." } });

  const bar = document.querySelector<HTMLElement>("[data-slot='approval-actions']");
  expect(bar).toBeTruthy();
  const classes = bar!.className.split(" ");
  for (const name of ["sticky", "bottom-0", "z-30", "-mx-4", "pb-[max(0.75rem,env(safe-area-inset-bottom))]", "border-t", "lg:static", "lg:mx-0"]) {
    expect(classes).toContain(name);
  }

  const buttons = within(bar!).getAllByRole("button");
  expect(buttons.map((button) => button.textContent)).toEqual(["Approve brief", "Send back", "Override flag"]);
  for (const button of buttons) {
    expect(button.className).toContain("max-lg:h-11");
  }
  expect(classes).toContain("grid-cols-2");
  expect(buttons[0].className.split(" ")).toContain("col-span-2");
  expect(buttons[1].className.split(" ")).not.toContain("col-span-2");

  const sheet = await openSheet("Send back", "Send the brief back?");
  expect(sheet.getAttribute("data-side")).toBe("bottom");
  expect(sheet.className).toContain("pb-[env(safe-area-inset-bottom)]");
  expect(sheet.className).toContain("lg:max-w-md");
});
