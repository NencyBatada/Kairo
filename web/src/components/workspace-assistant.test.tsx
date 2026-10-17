import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { PageHeader } from "./page-header";
import { WorkspaceAssistant } from "./workspace-assistant";
import { workspace } from "@/lib/ask-kairo/fixtures.test-data";

vi.mock("next/navigation", () => ({ usePathname: () => "/acme/today" }));
vi.mock("@/components/workspace-live-provider", async original => ({ ...await original<typeof import("@/components/workspace-live-provider")>(), useLiveWorkspace: () => workspace }));
vi.mock("@/lib/ask-kairo/api", async original => ({ ...await original<typeof import("@/lib/ask-kairo/api")>(), history: vi.fn().mockResolvedValue([]) }));

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

test("opens the real labelled composer and closes on Escape", async () => {
  render(
    <WorkspaceAssistant>
      <PageHeader title="Today" />
    </WorkspaceAssistant>,
  );

  const trigger = screen.getByRole("button", { name: "Ask Kairo" });
  fireEvent.click(trigger);
  const panel = await screen.findByRole("dialog", { name: "Ask Kairo" });
  expect(panel.querySelector(".min-h-0.overflow-y-auto")).toBeTruthy();
  expect(within(panel).getByText(/Private to you in Acme/)).toBeTruthy();
  expect(within(panel).getByText(/Today ·/)).toBeTruthy();
  expect(within(panel).getByRole("textbox", { name: "Your question" })).toBeTruthy();
  await waitFor(() => expect(panel.contains(document.activeElement)).toBe(true));

  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("dialog", { name: "Ask Kairo" })).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});

test("docks the panel on wide screens and leaves the page interactive", async () => {
  const matchMedia = vi.fn().mockImplementation(() => ({
    matches: true,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }));
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: matchMedia,
  });
  render(
    <WorkspaceAssistant>
      <PageHeader title="Today" actions={<button type="button">Page action</button>} />
    </WorkspaceAssistant>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Ask Kairo" }));
  const panel = await screen.findByRole("complementary", { name: "Ask Kairo" });
  expect(matchMedia).toHaveBeenCalledWith("(min-width: 90rem)");
  expect(panel.querySelector(".min-h-0.overflow-y-auto")).toBeTruthy();
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(screen.getByRole("button", { name: "Page action" })).toBeTruthy();
  fireEvent.click(within(panel).getByRole("button", { name: "Close" }));
  expect(screen.queryByRole("complementary", { name: "Ask Kairo" })).toBeNull();
});

test("updates the panel area when a workspace page changes", async () => {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: vi.fn().mockImplementation(() => ({
      matches: true,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  });
  const view = render(
    <WorkspaceAssistant>
      <PageHeader title="Today" />
    </WorkspaceAssistant>,
  );
  fireEvent.click(screen.getByRole("button", { name: "Ask Kairo" }));
  const panel = await screen.findByRole("complementary", { name: "Ask Kairo" });
  expect(within(panel).getByText(/Today ·/)).toBeTruthy();

  view.rerender(
    <WorkspaceAssistant>
      <PageHeader title="Settings" />
    </WorkspaceAssistant>,
  );
  expect(within(panel).getByText(/Settings ·/)).toBeTruthy();
});

test("closing and reopening the panel retains the unsent question", async () => {
  render(<WorkspaceAssistant><PageHeader title="Today" /></WorkspaceAssistant>);
  const trigger = screen.getByRole("button", { name: "Ask Kairo" });
  fireEvent.click(trigger);
  fireEvent.change(await screen.findByLabelText("Your question"), { target: { value: "Keep my draft across panels" } });
  const dialog = screen.getByRole("dialog");
  fireEvent.keyDown(dialog, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  fireEvent.click(trigger);
  expect((await screen.findByLabelText("Your question") as HTMLTextAreaElement).value).toBe("Keep my draft across panels");
});
