import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { AddTopic, RemoveTopic } from "./topic-controls";

test("preserves the typed topic and Ignore selection when the service rejects a duplicate", async () => {
  const action = vi.fn().mockResolvedValue({ status: "error", message: "That topic is already on your list." });
  render(<AddTopic action={action} />);
  fireEvent.change(screen.getByLabelText("Topic"), { target: { value: "Winter boots" } });
  fireEvent.change(screen.getByLabelText("Preference"), { target: { value: "ignore" } });
  fireEvent.click(screen.getByRole("button", { name: "Add topic" }));
  await screen.findByRole("alert");
  expect((screen.getByLabelText("Topic") as HTMLInputElement).value).toBe("Winter boots");
  expect((screen.getByLabelText("Preference") as HTMLSelectElement).value).toBe("ignore");
  expect(action.mock.calls[0][1].get("kind")).toBe("ignore");
});

test("links server validation to its field", async () => {
  const action = vi.fn().mockResolvedValue({ status: "error", fieldErrors: { phrase: ["Choose a longer topic."] } });
  render(<AddTopic action={action} />);
  fireEvent.change(screen.getByLabelText("Topic"), { target: { value: "Rain shoes" } });
  fireEvent.click(screen.getByRole("button", { name: "Add topic" }));
  await screen.findByText("Choose a longer topic.");
  expect(screen.getByLabelText("Topic").getAttribute("aria-describedby")).toBe("topic-phrase-error");
});

test("removal explains its consequence and only writes after confirmation", async () => {
  const action = vi.fn().mockResolvedValue({ status: "saved", message: "Topic removed." });
  render(<RemoveTopic phrase="Winter boots" kind="ignore" action={action} />);
  fireEvent.click(screen.getByRole("button", { name: "Remove Winter boots" }));
  const dialog = await screen.findByRole("alertdialog");
  expect(within(dialog).getByText(/can appear again/)).toBeTruthy();
  expect(action).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove topic" }));
  expect(await screen.findByRole("status")).toHaveProperty("textContent", "Topic removed.");
});

test("shows a removal failure in the open confirmation", async () => {
  const action = vi.fn().mockResolvedValue({ status: "error", message: "You don't have permission to do that." });
  render(<RemoveTopic phrase="Rain shoes" kind="watch" action={action} />);
  fireEvent.click(screen.getByRole("button", { name: "Remove Rain shoes" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove topic" }));
  expect(await within(dialog).findByRole("alert")).toHaveProperty("textContent", "You don't have permission to do that.");
});

test("prevents repeated removal while the request is pending", async () => {
  let finish!: (value: { status: string; message: string }) => void;
  const action = vi.fn().mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
  render(<RemoveTopic phrase="Rain shoes" kind="watch" action={action} />);
  fireEvent.click(screen.getByRole("button", { name: "Remove Rain shoes" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove topic" }));
  await waitFor(() => expect((within(dialog).getByRole("button", { name: "Remove topic" }) as HTMLButtonElement).disabled).toBe(true));
  expect((within(dialog).getByRole("button", { name: "Cancel" }) as HTMLButtonElement).disabled).toBe(true);
  finish({ status: "error", message: "Please retry." });
  await within(dialog).findByRole("alert");
});
