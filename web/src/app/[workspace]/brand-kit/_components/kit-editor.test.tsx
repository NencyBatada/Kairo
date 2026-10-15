import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { KitEditor } from "./kit-editor";
import type { KitVersion } from "./types";

export const version: KitVersion = {
  number: 2, revision: 4, status: "draft", origin: "drafted", restoredFrom: null,
  content: { voice: { summary: "Friendly", sources: [{ kind: "page", ref: "https://brand.example/about" }] }, colours: [{ name: "Ember", hex: "#A8461F", role: "primary" }], rules: {}, guidelineFileIds: ["guideline-1"] },
  files: [], createdBy: "admin", updatedBy: "admin", createdAt: "2026-10-15T09:00:00Z", updatedAt: "2026-10-15T09:00:00Z", approvedAt: null, approvedBy: null,
};
const action = vi.fn(async () => ({ status: "error" as const, message: "Keep your edits and try again." }));
const props = { version, draftRevision: 4, canEdit: true, files: [], save: action, approve: action, discard: action };

test("saves edits without losing source evidence, file references or colours", async () => {
  render(<KitEditor {...props} />);
  fireEvent.change(screen.getByLabelText("Voice summary"), { target: { value: "Practical and friendly" } });
  fireEvent.click(screen.getByRole("button", { name: "Save draft" }));
  await waitFor(() => expect(action).toHaveBeenCalled());
  const data = action.mock.calls.at(-1) as unknown as [unknown, FormData];
  const content = JSON.parse(String(data[1].get("content")));
  expect(content.voice.summary).toBe("Practical and friendly");
  expect(content.voice.sources).toEqual(version.content.voice?.sources);
  expect(content.guidelineFileIds).toEqual(["guideline-1"]);
  expect(content.colours).toEqual(version.content.colours);
  expect(screen.getByLabelText("Voice summary").getAttribute("value") ?? (screen.getByLabelText("Voice summary") as HTMLTextAreaElement).value).toBe("Practical and friendly");
  expect((await screen.findByRole("alert")).textContent).toContain("Keep your edits");
});

test("members can review sources but cannot change or approve a kit", () => {
  render(<KitEditor {...props} canEdit={false} />);
  expect((screen.getByLabelText("Voice summary") as HTMLTextAreaElement).closest("fieldset")?.disabled).toBe(true);
  expect(screen.queryByRole("button", { name: "Save draft" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Approve version" })).toBeNull();
  expect(screen.getByRole("link", { name: /brand.example/ }).getAttribute("href")).toBe("https://brand.example/about");
});

test("unsaved changes block approval and live refreshes preserve local edits", () => {
  const { rerender } = render(<KitEditor {...props} />);
  fireEvent.change(screen.getByLabelText("Voice summary"), { target: { value: "My unsaved edit" } });
  expect((screen.getByRole("button", { name: "Approve version" }) as HTMLButtonElement).disabled).toBe(true);
  rerender(<KitEditor {...props} version={{ ...version, revision: 5 }} draftRevision={5} />);
  expect((screen.getByLabelText("Voice summary") as HTMLTextAreaElement).value).toBe("My unsaved edit");
  expect(screen.getByText(/changed elsewhere/)).toBeTruthy();
  expect((screen.getByRole("button", { name: "Save draft" }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Load latest version" }));
  expect((screen.getByLabelText("Voice summary") as HTMLTextAreaElement).value).toBe("Friendly");
});

test("adding and removing a colour edits the structured content", () => {
  render(<KitEditor {...props} />);
  fireEvent.click(screen.getByRole("button", { name: "Add colour" }));
  expect(screen.getByLabelText("Colour code 2")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Remove colour 2" }));
  expect(screen.queryByLabelText("Colour code 2")).toBeNull();
});

test("unsafe source references never become clickable links", () => {
  render(<KitEditor {...props} version={{ ...version, content: { voice: { sources: [{ kind: "page", ref: "javascript:alert(1)" }] } } }} />);
  expect(screen.queryByRole("link", { name: /javascript/ })).toBeNull();
});
