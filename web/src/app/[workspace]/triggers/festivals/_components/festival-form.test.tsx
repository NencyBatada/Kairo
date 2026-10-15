import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { event, regions } from "../fixtures.test-data";
import { FestivalForm } from "./festival-form";
import { RemoveFestival } from "./remove-festival";

test("edits event dates and state markets while retaining data after validation", async () => {
  render(<FestivalForm event={event} regions={regions} handle="acme" action={async () => ({ status: "error", fieldErrors: { endsOn: ["An event can last up to 14 days."] } })} />);
  expect(screen.queryByLabelText("Mumbai")).toBeNull();
  expect(screen.getByText(/1 trigger rule uses this event/)).toBeTruthy();
  fireEvent.change(screen.getByLabelText("Ends on"), { target: { value: "2026-11-30" } });
  fireEvent.click(screen.getByRole("button", { name: "Save event" }));
  await screen.findByText("An event can last up to 14 days.");
  expect((screen.getByLabelText("Ends on") as HTMLInputElement).value).toBe("2026-11-30");
  expect(screen.getByLabelText("Ends on").getAttribute("aria-invalid")).toBe("true");
});
test("an in-use event cannot be removed and explains how to unblock it", () => {
  const action = vi.fn(); render(<RemoveFestival name="Diwali" inUse action={action} />);
  expect((screen.getByRole("button", { name: "Remove event" }) as HTMLButtonElement).disabled).toBe(true);
  expect(screen.getByText(/Change or delete the trigger rules/)).toBeTruthy(); expect(action).not.toHaveBeenCalled();
});
test("removal confirms, focuses Cancel and retains conflicts in the dialog", async () => {
  const action = vi.fn(async () => ({ status: "error" as const, message: "A rule now uses this event." }));
  render(<RemoveFestival name="Diwali" inUse={false} action={action} />);
  fireEvent.click(screen.getByRole("button", { name: "Remove event" }));
  const dialog = await screen.findByRole("alertdialog");
  await waitFor(() => expect(document.activeElement?.textContent).toBe("Cancel")); expect(action).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove event" }));
  await screen.findByText("A rule now uses this event."); expect(screen.getByRole("alertdialog")).toBeTruthy();
});
