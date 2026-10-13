import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { DropdownMenu, DropdownMenuContent, DropdownMenuLinkItem, DropdownMenuTrigger } from "./dropdown-menu";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "./sheet";

test("a dropdown menu opens from its trigger, lists link items and closes on Escape", async () => {
  render(
    <DropdownMenu>
      <DropdownMenuTrigger>Acme Shoes</DropdownMenuTrigger>
      <DropdownMenuContent aria-label="Workspaces">
        <DropdownMenuLinkItem href="#acme" aria-current="page">
          Acme Shoes
        </DropdownMenuLinkItem>
        <DropdownMenuLinkItem href="#beta">Beta Bags</DropdownMenuLinkItem>
      </DropdownMenuContent>
    </DropdownMenu>,
  );

  const trigger = screen.getByRole("button", { name: "Acme Shoes" });
  fireEvent.click(trigger);
  const menu = await screen.findByRole("menu", { name: "Workspaces" });

  expect(within(menu).getByRole("menuitem", { name: "Acme Shoes" }).getAttribute("aria-current")).toBe("page");
  expect(within(menu).getByRole("menuitem", { name: "Beta Bags" }).getAttribute("href")).toBe("#beta");

  fireEvent.keyDown(menu, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("menu")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});

test("a sheet is a named dialog that holds focus, closes on Escape and returns focus", async () => {
  render(
    <Sheet>
      <SheetTrigger aria-label="Open navigation">Menu</SheetTrigger>
      <SheetContent side="left">
        <SheetTitle>Navigation</SheetTitle>
        <a href="#today">Today</a>
      </SheetContent>
    </Sheet>,
  );

  const trigger = screen.getByRole("button", { name: "Open navigation" });
  fireEvent.click(trigger);
  const sheet = await screen.findByRole("dialog", { name: "Navigation" });

  await waitFor(() => expect(sheet.contains(document.activeElement)).toBe(true));
  expect(within(sheet).getByRole("button", { name: "Close" })).toBeTruthy();

  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});
