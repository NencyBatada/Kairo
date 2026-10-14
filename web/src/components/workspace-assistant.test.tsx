import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { PageHeader } from "./page-header";
import { WorkspaceAssistant } from "./workspace-assistant";

vi.mock("next/navigation", () => ({ usePathname: () => "/acme/today" }));

beforeEach(() => {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: vi.fn().mockImplementation(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  });
});

test("only workspace pages offer Ask Kairo", () => {
  render(<PageHeader title="Welcome" />);
  expect(screen.queryByRole("button", { name: "Ask Kairo" })).toBeNull();
});

test("opens a labelled panel without a fake chat and closes on Escape", async () => {
  render(
    <WorkspaceAssistant>
      <PageHeader title="Today" />
    </WorkspaceAssistant>,
  );

  const trigger = screen.getByRole("button", { name: "Ask Kairo" });
  fireEvent.click(trigger);
  const panel = await screen.findByRole("dialog", { name: "Ask Kairo" });
  expect(within(panel).getByText(/conversations will be available/i)).toBeTruthy();
  expect(within(panel).getByText("Today")).toBeTruthy();
  expect(within(panel).queryByRole("textbox")).toBeNull();
  await waitFor(() => expect(panel.contains(document.activeElement)).toBe(true));

  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("dialog", { name: "Ask Kairo" })).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});

test("docks the panel on wide screens and leaves the page interactive", async () => {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: vi.fn().mockImplementation(() => ({
      matches: true,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  });
  render(
    <WorkspaceAssistant>
      <PageHeader title="Today" actions={<button type="button">Page action</button>} />
    </WorkspaceAssistant>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Ask Kairo" }));
  const panel = await screen.findByRole("complementary", { name: "Ask Kairo" });
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(screen.getByRole("button", { name: "Page action" })).toBeTruthy();
  fireEvent.click(within(panel).getByRole("button", { name: "Close" }));
  expect(screen.queryByRole("complementary", { name: "Ask Kairo" })).toBeNull();
});
