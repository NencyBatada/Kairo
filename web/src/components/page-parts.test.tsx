import { render, screen } from "@testing-library/react";
import { Compass, Sun } from "lucide-react";
import { expect, test } from "vitest";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { SiteHeader } from "@/components/site-header";

test("a page header is the page's h1, with its description and actions", () => {
  render(<PageHeader title="Settings" description="Workspace details and your team." actions={<button type="button">Ask Kairo</button>} />);

  expect(screen.getByRole("heading", { level: 1, name: "Settings" })).toBeTruthy();
  expect(screen.getByText("Workspace details and your team.")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Ask Kairo" })).toBeTruthy();
});

test("an empty state explains what will appear, under a section heading", () => {
  render(<EmptyState icon={Sun} title="Nothing needs you yet" description="Approvals waiting for you will show up here." />);

  expect(screen.getByRole("heading", { level: 2, name: "Nothing needs you yet" })).toBeTruthy();
  expect(screen.getByText("Approvals waiting for you will show up here.")).toBeTruthy();
});

test("an empty state can be the page's own heading", () => {
  render(<EmptyState as="h1" icon={Compass} title="We couldn't find that page" description="It may have moved." />);

  expect(screen.getByRole("heading", { level: 1, name: "We couldn't find that page" })).toBeTruthy();
});

test("the site header links the logo home and holds its actions", () => {
  render(
    <SiteHeader>
      <button type="button">Sign out</button>
    </SiteHeader>,
  );

  expect(screen.getByRole("link", { name: "Kairo" }).getAttribute("href")).toBe("/");
  expect(screen.getByRole("button", { name: "Sign out" })).toBeTruthy();
});
