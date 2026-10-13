import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { SignOutButton } from "@/components/sign-out-button";

const router = vi.hoisted(() => ({ replace: vi.fn(), refresh: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => router }));

afterEach(() => {
  vi.unstubAllGlobals();
});

test("ends the session with the request header, then goes to sign-in", async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal("fetch", fetchMock);

  render(<SignOutButton />);
  fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

  await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/sign-in"));
  expect(fetchMock).toHaveBeenCalledWith("/api/auth/sign-out", {
    method: "POST",
    headers: { "X-Kairo-Request": "1" },
  });
  expect(router.refresh).toHaveBeenCalled();
});
