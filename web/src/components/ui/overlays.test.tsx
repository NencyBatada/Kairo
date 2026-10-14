import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { DropdownMenu, DropdownMenuContent, DropdownMenuLinkItem, DropdownMenuTrigger } from "./dropdown-menu";
import { Sheet, SheetContent, SheetDescription, SheetTitle, SheetTrigger } from "./sheet";

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

test("a bottom sheet is a named dialog that pads for the home indicator and becomes a centred dialog from lg", async () => {
  render(
    <Sheet>
      <SheetTrigger>Send back</SheetTrigger>
      <SheetContent side="bottom">
        <SheetTitle>Send the brief back?</SheetTitle>
        <SheetDescription>It goes back for changes.</SheetDescription>
      </SheetContent>
    </Sheet>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Send back" }));
  const sheet = await screen.findByRole("dialog", { name: "Send the brief back?" });

  expect(sheet.getAttribute("data-side")).toBe("bottom");
  expect(sheet.getAttribute("aria-describedby")).toBeTruthy();
  for (const name of ["bottom-0", "inset-x-0", "rounded-t-xl", "pb-[env(safe-area-inset-bottom)]", "max-h-[90dvh]", "lg:m-auto", "lg:max-w-md"]) {
    expect(sheet.className.split(" ")).toContain(name);
  }
  expect(sheet.className).not.toContain("h-dvh");
});

test("left and right sheets keep their full-height drawer shape", async () => {
  render(
    <Sheet>
      <SheetTrigger>Menu</SheetTrigger>
      <SheetContent side="right">
        <SheetTitle>Ask Kairo</SheetTitle>
      </SheetContent>
    </Sheet>,
  );

  fireEvent.click(screen.getByRole("button", { name: "Menu" }));
  const sheet = await screen.findByRole("dialog", { name: "Ask Kairo" });
  for (const name of ["inset-y-0", "right-0", "h-dvh", "border-l"]) {
    expect(sheet.className.split(" ")).toContain(name);
  }
});
