import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";

import { Checkbox } from "./checkbox";
import { ChoiceChip } from "./choice-chip";
import { NativeSelect } from "./native-select";

test("a native select is found by its label and keeps its value", () => {
  render(
    <>
      <label htmlFor="currency">Currency</label>
      <NativeSelect id="currency" name="currency" defaultValue="INR">
        <option value="USD">USD</option>
        <option value="INR">INR</option>
      </NativeSelect>
    </>,
  );

  expect((screen.getByLabelText("Currency") as HTMLSelectElement).value).toBe("INR");
});

test("a checkbox submits its value only when checked", () => {
  render(
    <form data-testid="form">
      <label>
        <Checkbox name="duty" value="u1" defaultChecked />
        Neha Rao
      </label>
      <label>
        <Checkbox name="duty" value="u2" />
        Arjun Mehta
      </label>
    </form>,
  );

  fireEvent.click(screen.getByLabelText("Arjun Mehta"));

  expect(new FormData(screen.getByTestId("form") as HTMLFormElement).getAll("duty")).toEqual(["u1", "u2"]);
});

test("a choice chip is named by its text and toggles", () => {
  render(
    <ChoiceChip name="roles" value="approver">
      Approver
    </ChoiceChip>,
  );

  const chip = screen.getByLabelText("Approver") as HTMLInputElement;
  fireEvent.click(chip);

  expect(chip.checked).toBe(true);
});

test("role checkboxes send every chosen role as a named group", () => {
  render(
    <form data-testid="form">
      <RoleCheckboxes legend="Roles for the new member" defaultValue={["marketer"]} />
    </form>,
  );

  fireEvent.click(screen.getByLabelText("Approver"));

  expect(screen.getByRole("group", { name: "Roles for the new member" })).toBeTruthy();
  expect(new FormData(screen.getByTestId("form") as HTMLFormElement).getAll("roles")).toEqual(["marketer", "approver"]);
});

test("form status announces success politely and errors assertively", () => {
  const { rerender } = render(<FormStatus state={{ status: "saved", message: "Saved." }} />);
  expect(screen.getByRole("status").textContent).toBe("Saved.");

  rerender(<FormStatus state={{ status: "error", message: "Something went wrong. Please try again." }} />);
  expect(screen.getByRole("alert").textContent).toBe("Something went wrong. Please try again.");
});

test("field errors read as one sentence under their field", () => {
  render(<FieldError id="email-error" errors={["Enter an email.", "It must be unique."]} />);

  expect(screen.getByText("Enter an email. It must be unique.").closest("p")?.id).toBe("email-error");
});
