import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { AccountPicker } from "./account-picker";

test("keeps duplicate account names distinguishable by their IDs", () => {
  render(
    <AccountPicker
      accounts={[
        { id: "111", name: "Footwear", managerId: null },
        { id: "222", name: "Footwear", managerId: null },
      ]}
      action={vi.fn(async () => ({ status: "saved" as const }))}
    />,
  );

  const options = screen.getAllByRole("option", { name: /Footwear/ });
  expect(options.map((option) => option.getAttribute("value"))).toEqual(["111", "222"]);
  expect(options.map((option) => option.textContent)).toEqual(["Footwear (111)", "Footwear (222)"]);
});

test("explains an empty account list and disables selection", () => {
  render(<AccountPicker accounts={[]} action={vi.fn(async () => ({ status: "saved" as const }))} />);
  expect(screen.getByText(/no accounts/i)).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Use account" })).toBeNull();
});

test("submits the selected external account ID", async () => {
  let submitted: FormData | null = null;
  const action = vi.fn(async (state: FormState, formData: FormData) => {
    expect(state).toEqual({ status: "idle" });
    submitted = formData;
    return { status: "saved" as const, message: "Account connected." };
  });
  render(<AccountPicker accounts={[{ id: "111", name: "Footwear", managerId: null }]} action={action} />);
  fireEvent.change(screen.getByRole("combobox", { name: "Account" }), { target: { value: "111" } });
  fireEvent.click(screen.getByRole("button", { name: "Use account" }));
  await vi.waitFor(() => expect(action).toHaveBeenCalled());
  expect((submitted as FormData | null)?.get("externalId")).toBe("111");
});
