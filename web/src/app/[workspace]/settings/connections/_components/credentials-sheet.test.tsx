import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { CredentialsSheet } from "./credentials-sheet";

const scopes = ["https://www.googleapis.com/auth/youtube.readonly", "https://www.googleapis.com/auth/youtube.upload"];

async function openSheet(action: (state: FormState, formData: FormData) => Promise<FormState>) {
  render(<CredentialsSheet productName="YouTube" scopes={scopes} action={action} />);
  fireEvent.click(screen.getByRole("button", { name: "Use client credentials" }));
  return screen.findByRole("dialog", { name: "Connect YouTube with client credentials" });
}

function fill(dialog: HTMLElement) {
  fireEvent.change(within(dialog).getByLabelText("Client ID"), { target: { value: "brand.apps.googleusercontent.com" } });
  fireEvent.change(within(dialog).getByLabelText("Client secret"), { target: { value: "secret" } });
  fireEvent.change(within(dialog).getByLabelText("Refresh token"), { target: { value: "token" } });
}

test("lists the access the product needs and hides the secret and token as they are typed", async () => {
  const dialog = await openSheet(vi.fn(async () => ({ status: "saved" as const })));

  for (const scope of scopes) {
    expect(within(dialog).getByText(scope)).toBeTruthy();
  }
  expect(within(dialog).getByLabelText("Client ID").getAttribute("type")).toBe("text");
  expect(within(dialog).getByLabelText("Client secret").getAttribute("type")).toBe("password");
  expect(within(dialog).getByLabelText("Refresh token").getAttribute("type")).toBe("password");
  for (const label of ["Client ID", "Client secret", "Refresh token"]) {
    expect(within(dialog).getByLabelText(label).getAttribute("autocomplete")).toBe("off");
  }
});

test("submits the three values and closes once they are accepted", async () => {
  const action = vi.fn<(state: FormState, formData: FormData) => Promise<FormState>>(async () => ({ status: "saved", message: "Credentials accepted." }));
  const dialog = await openSheet(action);
  fill(dialog);

  fireEvent.click(within(dialog).getByRole("button", { name: "Connect with credentials" }));

  await waitFor(() => expect(action).toHaveBeenCalledOnce());
  const formData = action.mock.calls[0][1];
  expect(formData.get("clientId")).toBe("brand.apps.googleusercontent.com");
  expect(formData.get("clientSecret")).toBe("secret");
  expect(formData.get("refreshToken")).toBe("token");
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
});

test("stays open and shows why Google rejected the credentials", async () => {
  const dialog = await openSheet(vi.fn(async () => ({ status: "error" as const, message: "Google rejected these credentials." })));
  fill(dialog);

  fireEvent.click(within(dialog).getByRole("button", { name: "Connect with credentials" }));

  expect((await within(dialog).findByRole("alert")).textContent).toContain("Google rejected these credentials.");
  expect(screen.getByRole("dialog")).toBeTruthy();
});

test("shows field errors under the matching control", async () => {
  const dialog = await openSheet(vi.fn(async () => ({ status: "error" as const, fieldErrors: { refreshToken: ["Enter the refresh token."] } })));
  fill(dialog);

  fireEvent.click(within(dialog).getByRole("button", { name: "Connect with credentials" }));

  expect(await within(dialog).findByText("Enter the refresh token.")).toBeTruthy();
  expect(within(dialog).getByLabelText("Refresh token").getAttribute("aria-invalid")).toBe("true");
});
