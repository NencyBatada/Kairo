import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { NavMenu } from "@/components/nav-menu";

test("lists the seven areas in order, each linking to its screen", () => {
  render(<NavMenu />);

  const nav = screen.getByRole("navigation", { name: "Main" });
  const links = within(nav).getAllByRole("link");

  expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Today", "/today"],
    ["Opportunities", "/opportunities"],
    ["Campaigns", "/campaigns"],
    ["Triggers", "/triggers"],
    ["Impact Lab", "/impact-lab"],
    ["Brand Kit", "/brand-kit"],
    ["Settings", "/settings"],
  ]);
});
