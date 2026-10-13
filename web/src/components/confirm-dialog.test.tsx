import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";

function renderDialog(action = vi.fn()) {
  render(
    <ConfirmDialog
      trigger={<Button variant="destructive">Remove</Button>}
      title="Remove Priya Shah?"
      description="Priya Shah loses access to this workspace straight away."
      confirmLabel="Remove"
      action={action}
    />,
  );
  return action;
}

test("asks first, starting on Cancel, and does nothing when cancelled", async () => {
  const action = renderDialog();

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  const cancel = within(dialog).getByRole("button", { name: "Cancel" });

  await waitFor(() => expect(document.activeElement).toBe(cancel));
  fireEvent.click(cancel);

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(action).not.toHaveBeenCalled();
});

test("runs the action when confirmed, then closes", async () => {
  const action = renderDialog();

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove" }));

  await waitFor(() => expect(action).toHaveBeenCalledWith(expect.any(FormData)));
  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
});
