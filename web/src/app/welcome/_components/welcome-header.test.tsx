import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { WelcomeHeader } from "./welcome-header";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn(), refresh: vi.fn() }) }));

test("lets someone without a workspace sign out", () => {
  render(<WelcomeHeader />);

  expect(screen.getByRole("link", { name: "Kairo" }).getAttribute("href")).toBe("/");
  expect(screen.getByRole("button", { name: "Sign out" })).toBeTruthy();
});
