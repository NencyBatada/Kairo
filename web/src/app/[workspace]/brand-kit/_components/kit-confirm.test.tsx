import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { KitConfirm } from "./kit-confirm";

test("approval is deliberate, carries the displayed revision and retains failures", async () => {
  const action = vi.fn(async () => ({ status: "error" as const, message: "The draft changed. Review it again." }));
  render(<KitConfirm label="Approve version" title="Approve version 2?" description="Agents will use this saved kit." action={action} revision={4} />);
  fireEvent.click(screen.getByRole("button", { name: "Approve version" }));
  const dialog = await screen.findByRole("alertdialog");
  expect(action).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "Approve version" }));
  await waitFor(() => expect(action).toHaveBeenCalledOnce());
  expect((action.mock.calls[0] as unknown as [unknown, FormData])[1].get("revision")).toBe("4");
  expect((await within(dialog).findByRole("alert")).textContent).toContain("draft changed");
  expect(screen.getByRole("alertdialog")).toBeTruthy();
});

test.each(["Approve version", "Discard draft"])("a live refresh invalidates the open %s confirmation", async (label) => {
  const action = vi.fn(async () => ({ status: "error" as const, message: "Stale revision" }));
  const props = { label, title: "Confirm version 2?", description: "This changes the saved kit.", action };
  const { rerender } = render(<KitConfirm {...props} revision={4} />);
  fireEvent.click(screen.getByRole("button", { name: label }));
  const dialog = await screen.findByRole("alertdialog");
  rerender(<KitConfirm {...props} revision={5} />);
  expect((within(dialog).getByRole("button", { name: label }) as HTMLButtonElement).disabled).toBe(true);
  expect(within(dialog).getByRole("alert").textContent).toContain("changed");
  expect(action).not.toHaveBeenCalled();
});
