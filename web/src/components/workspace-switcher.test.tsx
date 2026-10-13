import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { WorkspaceSwitcher } from "@/components/workspace-switcher";

const acme = { id: "1", handle: "acme", name: "Acme Shoes" };
const beta = { id: "2", handle: "beta", name: "Beta Bags" };

test("links to every workspace and marks the current one", () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme, beta]} pendingInvitations={0} />);

  const nav = screen.getByRole("navigation", { name: "Workspaces" });
  const current = within(nav).getByRole("link", { name: "Acme Shoes" });
  expect(current.getAttribute("href")).toBe("/acme/today");
  expect(current.getAttribute("aria-current")).toBe("page");
  expect(within(nav).getByRole("link", { name: "Beta Bags" }).getAttribute("aria-current")).toBeNull();
});

test("shows how many invitations are waiting", () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme]} pendingInvitations={2} />);

  expect(screen.getByRole("link", { name: "Invitations (2) and new workspaces" }).getAttribute("href")).toBe("/welcome");
});
