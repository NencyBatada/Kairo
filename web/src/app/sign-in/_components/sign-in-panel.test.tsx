import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { SignInPanel } from "./sign-in-panel";

async function renderPanel(searchParams: Record<string, string>) {
  render(await SignInPanel({ searchParams: Promise.resolve(searchParams) }));
}

test("sends the user to Google with the page they asked for", async () => {
  await renderPanel({ returnUrl: "/acme/today?tab=open" });

  expect(screen.getByRole("link", { name: "Sign in with Google" }).getAttribute("href")).toBe(
    "/api/auth/sign-in?returnUrl=%2Facme%2Ftoday%3Ftab%3Dopen",
  );
  expect(screen.queryByRole("alert")).toBeNull();
});

test("explains why an unverified account was turned away", async () => {
  await renderPanel({ error: "unverified_email" });

  expect(screen.getByRole("alert").textContent).toContain("hasn't verified the email");
});

test("treats an unknown error as a failed sign-in", async () => {
  await renderPanel({ error: "something_else" });

  expect(screen.getByRole("alert").textContent).toBe("Sign-in didn't finish. Please try again.");
});
