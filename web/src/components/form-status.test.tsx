import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { FormStatus } from "./form-status";

test("field-only validation announces a bounded summary without moving focus", () => {
  render(<><input aria-label="Current field" autoFocus /><FormStatus state={{ status: "error", fieldErrors: { name: ["Use the registered name."] } }} /></>);
  expect(screen.getByRole("alert").textContent).toBe("Check the highlighted fields and try again.");
  expect(document.activeElement).toBe(screen.getByLabelText("Current field"));
});

test("empty field errors do not produce a misleading error announcement", () => {
  render(<FormStatus state={{ status: "error", fieldErrors: { name: [] } }} />);
  expect(screen.queryByRole("alert")).toBeNull();
});
