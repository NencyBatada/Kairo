import { render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { NavMenu } from "@/components/nav-menu";

const navigation = vi.hoisted(() => ({ pathname: "/acme/campaigns/42/brief" }));

vi.mock("next/navigation", () => ({ usePathname: () => navigation.pathname }));

beforeEach(() => {
  navigation.pathname = "/acme/campaigns/42/brief";
});

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

test("marks the area the current page belongs to", () => {
  render(<NavMenu workspace="acme" />);

  expect(screen.getByRole("link", { name: "Campaigns" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("link", { name: "Today" }).getAttribute("aria-current")).toBeNull();
});

test("the Brand Kit area exposes Catalogue without adding an eighth main area", () => {
  navigation.pathname = "/acme/brand-kit";
  render(<NavMenu workspace="acme" />);
  expect(screen.getByRole("link", { name: "Catalogue" }).getAttribute("href")).toBe("/acme/brand-kit/catalogue");
});
