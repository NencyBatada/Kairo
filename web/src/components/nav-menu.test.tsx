import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { NavMenu } from "@/components/nav-menu";

test("lists the seven areas in order, each linking into the workspace", () => {
  render(<NavMenu workspace="acme" />);

  const nav = screen.getByRole("navigation", { name: "Main" });
  const links = within(nav).getAllByRole("link");

  expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Today", "/acme/today"],
    ["Opportunities", "/acme/opportunities"],
    ["Campaigns", "/acme/campaigns"],
    ["Triggers", "/acme/triggers"],
    ["Impact Lab", "/acme/impact-lab"],
    ["Brand Kit", "/acme/brand-kit"],
    ["Settings", "/acme/settings"],
  ]);
});
