import { render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { SettingsNav } from "./settings-nav";

vi.mock("next/navigation", () => ({ usePathname: () => null }));

test("links to the five settings pages of the workspace", () => {
  render(<SettingsNav workspace="acme" />);

  const nav = screen.getByRole("navigation", { name: "Settings" });
  expect(within(nav).getAllByRole("link").map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Workspace", "/acme/settings"],
    ["Connections", "/acme/settings/connections"],
    ["Team", "/acme/settings/team"],
    ["Approvals", "/acme/settings/approvals"],
    ["Audit log", "/acme/settings/audit-log"],
  ]);
});
