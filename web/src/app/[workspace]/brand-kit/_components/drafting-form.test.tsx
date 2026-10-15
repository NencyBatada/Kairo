import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { DraftingForm } from "./drafting-form";
import type { KitRun } from "./types";

const run: KitRun = { id: "run", status: "running", websiteUrl: "https://brand.example", pages: [], ads: [], error: null, requestedBy: "admin", requestedAt: "2026-10-15T09:00:00Z", finishedAt: null, version: null };
const action = vi.fn();
test("requires explicit replacement consent when a draft exists", () => {
  render(<DraftingForm run={null} hasDraft canEdit action={action} />);
  expect((screen.getByRole("button", { name: "Draft from website" }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByLabelText(/Replace the current draft/));
  expect((screen.getByRole("button", { name: "Draft from website" }) as HTMLButtonElement).disabled).toBe(false);
});
test("prevents duplicate drafting runs and shows live progress", () => {
  render(<DraftingForm run={run} hasDraft={false} canEdit action={action} />);
  expect(screen.getByRole("status").textContent).toContain("Reading your website");
  expect((screen.getByRole("button", { name: "Draft from website" }) as HTMLButtonElement).disabled).toBe(true);
});
test("shows failure recovery and keeps the website after a failed submission", async () => {
  render(<DraftingForm run={{ ...run, status: "failed", error: { code: "site_unreadable", message: "Fill in the Brand Kit by hand." } }} hasDraft={false} canEdit action={vi.fn(async () => ({ status: "error" as const, message: "Try again later." }))} />);
  fireEvent.change(screen.getByLabelText("Brand website"), { target: { value: "https://brand.example" } });
  fireEvent.click(screen.getByRole("button", { name: "Draft from website" }));
  expect(await screen.findByText("Try again later.")).toBeTruthy();
  expect((screen.getByLabelText("Brand website") as HTMLInputElement).value).toBe("https://brand.example");
});
