import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { UserMenu } from "@/components/user-menu";

const router = vi.hoisted(() => ({ replace: vi.fn(), refresh: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => router }));

afterEach(() => {
  vi.unstubAllGlobals();
});

test("shows who is signed in and signs them out from the menu", async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal("fetch", fetchMock);
  render(<UserMenu name="Neha Kapoor" email="neha@acme.example" pictureUrl={null} />);

  fireEvent.click(screen.getByRole("button", { name: "Account: Neha Kapoor" }));
  const menu = await screen.findByRole("menu", { name: "Account" });
  expect(within(menu).getByText("neha@acme.example")).toBeTruthy();

  fireEvent.click(within(menu).getByRole("menuitem", { name: "Sign out" }));

  await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/sign-in"));
  expect(fetchMock).toHaveBeenCalledWith("/api/auth/sign-out", { method: "POST", headers: { "X-Kairo-Request": "1" } });
});
