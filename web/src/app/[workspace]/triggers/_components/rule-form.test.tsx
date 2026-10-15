import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { options, festival, rule } from "../fixtures.test-data";
import { readRule } from "../_lib/rules";
import { RuleForm } from "./rule-form";
import { DeleteRule } from "./rule-controls";

test("requires preview before saving and invalidates it when a field changes", async () => {
  const action = vi.fn(async (_previous, data: FormData) => ({ status: "idle" as const, preview: { request: readRule(data).request, description: "Rain makes an opportunity." } }));
  render(<RuleForm action={action} options={options} festivals={[festival]} handle="acme" rule={rule} />);
  expect(screen.queryByRole("button", { name: "Save rule" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Preview rule" }));
  await screen.findByText("Rain makes an opportunity.");
  expect(screen.getByRole("button", { name: "Save rule" })).toBeTruthy();
  expect((screen.getByLabelText("Rule name") as HTMLInputElement).value).toBe(rule.name);
  fireEvent.change(screen.getByLabelText("Rule name"), { target: { value: "Updated rain rule" } });
  expect(screen.queryByRole("button", { name: "Save rule" })).toBeNull();
});
test("switches sources and units, showing only the relevant condition fields", () => {
  render(<RuleForm action={vi.fn()} options={options} festivals={[festival]} handle="acme" />);
  expect(screen.queryByLabelText("Maharashtra")).toBeNull();
  fireEvent.change(screen.getByLabelText("Weather condition"), { target: { value: "heatwave" } });
  expect((screen.getByLabelText("Threshold (°C)") as HTMLInputElement).value).toBe("40");
  fireEvent.change(screen.getByLabelText("Source"), { target: { value: "festival" } });
  expect(screen.queryByLabelText(/Threshold/)).toBeNull();
  expect(screen.getByLabelText("Festival or event")).toBeTruthy();
  expect(screen.getByLabelText("Maharashtra")).toBeTruthy();
  fireEvent.change(screen.getByLabelText("Source"), { target: { value: "stock" } });
  expect(screen.queryByLabelText("Festival or event")).toBeNull();
  expect(screen.getByLabelText("Minimum units in stock")).toBeTruthy();
});
test("keeps values and associates server validation with its field", async () => {
  render(<RuleForm action={async () => ({ status: "error", fieldErrors: { "condition.threshold": ["Use 10 to 300 mm."] } })} options={options} festivals={[]} handle="acme" rule={rule} />);
  fireEvent.click(screen.getByRole("button", { name: "Preview rule" }));
  await screen.findByText("Use 10 to 300 mm.");
  expect(screen.getByLabelText("Threshold (mm)").getAttribute("aria-invalid")).toBe("true");
  expect((screen.getByLabelText("Rule name") as HTMLInputElement).value).toBe(rule.name);
});
test("delete requires confirmation and keeps service conflicts in the dialog", async () => {
  const action = vi.fn(async () => ({ status: "error" as const, message: "Please try again." }));
  render(<DeleteRule name="Rain rule" action={action} />);
  fireEvent.click(screen.getByRole("button", { name: "Delete rule" }));
  expect(action).not.toHaveBeenCalled();
  const dialog = await screen.findByRole("alertdialog", { name: "Delete Rain rule?" });
  await waitFor(() => expect(document.activeElement?.textContent).toBe("Cancel"));
  fireEvent.submit(dialog.querySelector("form")!);
  await screen.findByText("Please try again.");
  expect(screen.getByRole("alertdialog")).toBeTruthy();
});
