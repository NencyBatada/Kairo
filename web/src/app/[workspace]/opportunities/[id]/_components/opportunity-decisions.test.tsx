import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { OpportunityDecisions } from "./opportunity-decisions";

const props = () => ({ status: "new", available: true, canDecide: true, accept: vi.fn().mockResolvedValue({ status: "error", message: "Already accepted." }), save: vi.fn().mockResolvedValue({ status: "saved", opportunityStatus: "saved", message: "Opportunity saved." }), unsave: vi.fn().mockResolvedValue({ status: "saved", opportunityStatus: "new", message: "Opportunity removed from Saved." }), dismiss: vi.fn().mockResolvedValue({ status: "saved" }) });

test("disables acceptance when matching products lose stock, keeping save available", () => {
  render(<OpportunityDecisions {...props()} available={false} />);
  expect((screen.getByRole("button", { name: "Accept opportunity" }) as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole("button", { name: "Save opportunity" }) as HTMLButtonElement).disabled).toBe(false);
});

test("save switches to unsave after a successful action", async () => {
  const actions = props();
  render(<OpportunityDecisions {...actions} />);
  fireEvent.click(screen.getByRole("button", { name: "Save opportunity" }));
  await screen.findByRole("button", { name: "Remove from Saved" });
  await waitFor(() => expect((screen.getByRole("button", { name: "Remove from Saved" }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole("button", { name: "Remove from Saved" }));
  await screen.findByRole("button", { name: "Save opportunity" });
  expect(actions.save).toHaveBeenCalledTimes(1);
  expect(actions.unsave).toHaveBeenCalledTimes(1);
});

test("accept asks for confirmation and retains backend conflicts inside the dialog", async () => {
  const actions = props();
  render(<OpportunityDecisions {...actions} />);
  fireEvent.click(screen.getByRole("button", { name: "Accept opportunity" }));
  const dialog = await screen.findByRole("alertdialog");
  expect(actions.accept).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "Accept opportunity" }));
  expect(await within(dialog).findByRole("alert")).toHaveProperty("textContent", "Already accepted.");
  expect(screen.getByRole("alertdialog")).toBeTruthy();
});

test("dismiss requires a reason, makes an Other note required, and announces success", async () => {
  const actions = props();
  render(<OpportunityDecisions {...actions} />);
  fireEvent.click(screen.getByRole("button", { name: "Dismiss opportunity" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.change(within(dialog).getByLabelText("Reason"), { target: { value: "other" } });
  const note = within(dialog).getByLabelText("Note") as HTMLTextAreaElement;
  expect(note.required).toBe(true);
  fireEvent.change(note, { target: { value: "We already have a campaign." } });
  fireEvent.click(within(dialog).getByRole("button", { name: "Dismiss opportunity" }));
  await screen.findByRole("status");
  expect(screen.queryByRole("button", { name: "Accept opportunity" })).toBeNull();
  expect(actions.dismiss.mock.calls[0][1].get("note")).toBe("We already have a campaign.");
});

test("keeps the note after dismiss validation fails", async () => {
  const actions = props(); actions.dismiss.mockResolvedValue({ status: "error", message: "Try again." });
  render(<OpportunityDecisions {...actions} />);
  fireEvent.click(screen.getByRole("button", { name: "Dismiss opportunity" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.change(within(dialog).getByLabelText("Reason"), { target: { value: "wrong_timing" } });
  fireEvent.change(within(dialog).getByLabelText("Note (optional)"), { target: { value: "Next quarter" } });
  fireEvent.click(within(dialog).getByRole("button", { name: "Dismiss opportunity" }));
  await within(dialog).findByRole("alert");
  expect((within(dialog).getByLabelText("Note (optional)") as HTMLTextAreaElement).value).toBe("Next quarter");
});

test("holds confirmation open and prevents duplicate submissions while pending", async () => {
  const actions = props();
  let finish!: (value: { status: string; message: string }) => void;
  actions.accept.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
  render(<OpportunityDecisions {...actions} />);
  fireEvent.click(screen.getByRole("button", { name: "Accept opportunity" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Accept opportunity" }));
  await waitFor(() => expect((within(dialog).getByRole("button", { name: "Accept opportunity" }) as HTMLButtonElement).disabled).toBe(true));
  expect((within(dialog).getByRole("button", { name: "Cancel" }) as HTMLButtonElement).disabled).toBe(true);
  finish({ status: "error", message: "Please retry." });
  await within(dialog).findByRole("alert");
});
