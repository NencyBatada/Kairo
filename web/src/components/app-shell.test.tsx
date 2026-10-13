import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { AppShell } from "@/components/app-shell";

test("lets keyboard users skip straight to the page", () => {
  render(
    <AppShell sidebar={<p>Sidebar</p>}>
      <p>Page</p>
    </AppShell>,
  );

  expect(screen.getByRole("link", { name: "Skip to content" }).getAttribute("href")).toBe("#main");
  expect(screen.getByRole("main").id).toBe("main");
  expect(screen.getByRole("main").textContent).toBe("Page");
});
