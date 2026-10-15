import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { product } from "../_lib/fixtures.test-data";
import { ProductFixes } from "./product-fixes";

test("only missing source fields can be filled, and stock is corrected in the source", () => {
  render(<ProductFixes product={product} canFix action={vi.fn()} />);
  expect((screen.getByLabelText("Title") as HTMLInputElement).disabled).toBe(true);
  expect((screen.getByLabelText("Price") as HTMLInputElement).disabled).toBe(false);
  expect(screen.queryByRole("textbox", { name: "Stock status" })).toBeNull();
  expect(screen.getByText(/Correct stock in the file or feed/)).toBeTruthy();
});
test("failed saves retain edited values and show validation at the price field", async () => {
  const action = vi.fn(async () => ({ status: "error" as const, fieldErrors: { price: ["Enter a valid price."] } }));
  render(<ProductFixes product={product} canFix action={action} />);
  fireEvent.change(screen.getByLabelText("Price"), { target: { value: "1499" } });
  fireEvent.click(screen.getByRole("button", { name: "Save missing details" }));
  await waitFor(() => expect(screen.getByLabelText("Price").getAttribute("aria-invalid")).toBe("true"));
  expect((screen.getByLabelText("Price") as HTMLInputElement).value).toBe("1499");
});
test("an existing fix can be cleared and approvers have no save controls", () => {
  const { rerender } = render(<ProductFixes product={{ ...product, price: 1499, fixed: ["price"] }} canFix action={vi.fn()} />);
  expect(screen.getByLabelText("Clear price fix")).toBeTruthy();
  fireEvent.click(screen.getByLabelText("Clear price fix"));
  expect((screen.getByLabelText("Price") as HTMLInputElement).disabled).toBe(true);
  rerender(<ProductFixes product={product} canFix={false} action={vi.fn()} />);
  expect(screen.queryByRole("button", { name: "Save missing details" })).toBeNull();
});
test("source updates preserve local edits and require reviewing the latest product", () => {
  const { rerender } = render(<ProductFixes product={product} canFix action={vi.fn()} />);
  fireEvent.change(screen.getByLabelText("Price"), { target: { value: "1999" } });
  rerender(<ProductFixes product={{ ...product, price: 1599, updatedAt: "2026-10-15T10:00:00Z" }} canFix action={vi.fn()} />);
  expect((screen.getByLabelText("Price") as HTMLInputElement).value).toBe("1999");
  expect((screen.getByRole("button", { name: "Save missing details" }) as HTMLButtonElement).disabled).toBe(true);
});
