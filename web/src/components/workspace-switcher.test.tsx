import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { WorkspaceSwitcher } from "@/components/workspace-switcher";

const acme = { id: "1", handle: "acme", name: "Acme Shoes" };
const beta = { id: "2", handle: "beta", name: "Beta Bags" };

async function openMenu(name: string) {
  fireEvent.click(screen.getByRole("button", { name: `Workspace: ${name}` }));
  return screen.findByRole("menu", { name: "Workspaces" });
}

test("names the current workspace on its button, in full even when the text is cut short", () => {
  const long = { ...acme, name: "Acme Footwear and Monsoon Accessories Private Limited, Western India Division" };
  render(<WorkspaceSwitcher current={long} workspaces={[long, beta]} pendingInvitations={0} />);

  expect(screen.getByRole("button", { name: `Workspace: ${long.name}` }).getAttribute("title")).toBe(long.name);
});

test("shows the workspace's first character whole on its tile", () => {
  const rocket = { ...acme, name: "🚀 Rocket Co" };
  render(<WorkspaceSwitcher current={rocket} workspaces={[rocket]} pendingInvitations={0} />);

  expect(screen.getByText("🚀")).toBeTruthy();
});

test("links to every workspace and marks the current one", async () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme, beta]} pendingInvitations={0} />);

  const menu = await openMenu("Acme Shoes");
  const current = within(menu).getByRole("menuitem", { name: "Acme Shoes" });

  expect(current.getAttribute("href")).toBe("/acme/today");
  expect(current.getAttribute("aria-current")).toBe("page");
  expect(within(menu).getByRole("menuitem", { name: "Beta Bags" }).getAttribute("aria-current")).toBeNull();
  expect(within(menu).getByRole("menuitem", { name: "Create a workspace" }).getAttribute("href")).toBe("/welcome");
});

test("shows how many invitations are waiting", async () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme]} pendingInvitations={2} />);

  const menu = await openMenu("Acme Shoes");

  expect(within(menu).getByRole("menuitem", { name: "Invitations (2) and new workspaces" }).getAttribute("href")).toBe("/welcome");
});
