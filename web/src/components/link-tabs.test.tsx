import { render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { isCurrentPath, LinkTabs } from "@/components/link-tabs";

const navigation = vi.hoisted(() => ({ pathname: "/" }));

vi.mock("next/navigation", () => ({ usePathname: () => navigation.pathname }));

const tabs = [
  { href: "/acme/settings", label: "Workspace", exact: true },
  { href: "/acme/settings/team", label: "Team" },
];

beforeEach(() => {
  navigation.pathname = "/acme/settings/team";
});

test("marks the tab for the current page", () => {
  render(<LinkTabs label="Settings" tabs={tabs} />);

  const nav = screen.getByRole("navigation", { name: "Settings" });
  expect(within(nav).getByRole("link", { name: "Team" }).getAttribute("aria-current")).toBe("page");
  expect(within(nav).getByRole("link", { name: "Workspace" }).getAttribute("aria-current")).toBeNull();
});

test("an exact tab is current only on its own page", () => {
  navigation.pathname = "/acme/settings";
  render(<LinkTabs label="Settings" tabs={tabs} />);

  expect(screen.getByRole("link", { name: "Workspace" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("link", { name: "Team" }).getAttribute("aria-current")).toBeNull();
});

test("a tab stays current on the pages below it, and only there", () => {
  expect(isCurrentPath("/acme/campaigns/42/brief/edit", "/acme/campaigns/42/brief")).toBe(true);
  expect(isCurrentPath("/acme/campaigns-archive", "/acme/campaigns")).toBe(false);
  expect(isCurrentPath("/acme/settings/team", "/acme/settings", true)).toBe(false);
  expect(isCurrentPath(null, "/acme/today")).toBe(false);
});
