import { expect, test, vi } from "vitest";

import { unwrap } from "./client";

vi.mock("server-only", () => ({}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`redirect:${url}`);
  },
  notFound: () => {
    throw new Error("not-found");
  },
}));

vi.mock("next/headers", () => ({
  headers: async () => new Headers({ "x-kairo-path": "/acme/today?tab=open" }),
  cookies: async () => ({ get: () => undefined }),
}));

test("sends the user to sign-in, and back to the same page, when the session has ended", async () => {
  await expect(unwrap({ response: new Response(null, { status: 401 }) })).rejects.toThrow(
    "redirect:/sign-in?returnUrl=%2Facme%2Ftoday%3Ftab%3Dopen",
  );
});

test("renders not found for a 404", async () => {
  await expect(unwrap({ response: new Response(null, { status: 404 }) })).rejects.toThrow("not-found");
});

test("returns the data of a successful answer", async () => {
  expect(await unwrap({ data: { id: 1 }, response: new Response(null, { status: 200 }) })).toEqual({ id: 1 });
});

test("fails loudly on any other answer", async () => {
  await expect(unwrap({ response: new Response(null, { status: 500 }) })).rejects.toThrow("The core API answered 500.");
});
