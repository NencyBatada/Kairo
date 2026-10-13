import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { MobileNav } from "@/components/mobile-nav";

function renderNav() {
  render(
    <MobileNav context={<span>Acme Shoes</span>}>
      <nav aria-label="Main">
        <a href="#today">Today</a>
      </nav>
    </MobileNav>,
  );
  return screen.getByRole("button", { name: "Open navigation" });
}

test("shows the workspace on the top bar and opens the navigation in a drawer", async () => {
  const button = renderNav();

  expect(screen.getByText("Acme Shoes")).toBeTruthy();
  fireEvent.click(button);

  const drawer = await screen.findByRole("dialog", { name: "Navigation" });
  expect(within(drawer).getByRole("navigation", { name: "Main" })).toBeTruthy();
});

test("closes once a page is chosen", async () => {
  fireEvent.click(renderNav());
  const drawer = await screen.findByRole("dialog", { name: "Navigation" });

  fireEvent.click(within(drawer).getByRole("link", { name: "Today" }));

  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
});

test("closes on Escape and gives focus back to the menu button", async () => {
  const button = renderNav();
  fireEvent.click(button);
  await screen.findByRole("dialog", { name: "Navigation" });

  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });

  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(button));
});
