import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { ConnectionControls } from "./connection-controls";

function renderControls(
  connected = true,
  check: (state: FormState, formData: FormData) => Promise<FormState> = vi.fn(async () => ({ status: "saved" as const })),
  disconnect: (state: FormState, formData: FormData) => Promise<FormState> = vi.fn(async () => ({ status: "saved" as const })),
) {
  render(<ConnectionControls connected={connected} productName="Google Ads" check={check} disconnect={disconnect} />);
  return { check, disconnect };
}

test("offers access checks only to connected products", () => {
  renderControls();
  expect(screen.getByRole("button", { name: "Check access" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Disconnect" })).toBeTruthy();
});

test("offers disconnect without an access check while account selection is incomplete", () => {
  renderControls(false);
  expect(screen.queryByRole("button", { name: "Check access" })).toBeNull();
  expect(screen.getByRole("button", { name: "Disconnect" })).toBeTruthy();
});

test("cancelling the confirmation never calls disconnect", async () => {
  const { disconnect } = renderControls();

  fireEvent.click(screen.getByRole("button", { name: "Disconnect" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Disconnect Google Ads?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(disconnect).not.toHaveBeenCalled();
});

test("disconnects only after confirmation and shows an action failure", async () => {
  const disconnect = vi.fn(async () => ({ status: "error" as const, message: "Disconnect failed." }));
  renderControls(true, vi.fn(async () => ({ status: "saved" as const })), disconnect);

  fireEvent.click(screen.getByRole("button", { name: "Disconnect" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Disconnect Google Ads?" });
  expect(disconnect).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "Disconnect" }));

  await waitFor(() => expect(disconnect).toHaveBeenCalledOnce());
  expect((await screen.findByRole("alert")).textContent).toContain("Disconnect failed.");
});

test("holds access check and disconnect controls while each request is pending", async () => {
  let finishCheck: ((value: FormState) => void) | undefined;
  let finishDisconnect: ((value: FormState) => void) | undefined;
  const check = vi.fn(() => new Promise<FormState>((resolve) => { finishCheck = resolve; }));
  const disconnect = vi.fn(() => new Promise<FormState>((resolve) => { finishDisconnect = resolve; }));
  renderControls(true, check, disconnect);

  fireEvent.click(screen.getByRole("button", { name: "Check access" }));
  await waitFor(() => expect(check).toHaveBeenCalledOnce());
  expect(screen.getByRole<HTMLButtonElement>("button", { name: "Check access" }).disabled).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Check access" }));
  expect(check).toHaveBeenCalledOnce();
  finishCheck?.({ status: "saved", message: "Access confirmed." });
  await screen.findByRole("status");

  fireEvent.click(screen.getByRole("button", { name: "Disconnect" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Disconnect Google Ads?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Disconnect" }));
  await waitFor(() => expect(disconnect).toHaveBeenCalledOnce());
  expect(screen.getByRole<HTMLButtonElement>("button", { name: "Disconnect" }).disabled).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Disconnect" }));
  expect(disconnect).toHaveBeenCalledOnce();
  finishDisconnect?.({ status: "saved", message: "Connection removed." });
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>("button", { name: "Disconnect" }).disabled).toBe(false));
});
