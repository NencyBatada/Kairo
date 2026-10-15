import { beforeEach, expect, test, vi } from "vitest";

import { fixProduct, refreshFeed, setFeed, stopFeed } from "./actions";

const { catalogueRequest, signInAgain, refresh } = vi.hoisted(() => ({ catalogueRequest: vi.fn(), signInAgain: vi.fn(), refresh: vi.fn() }));
vi.mock("./_lib/api", () => ({ catalogueRequest }));
vi.mock("@/lib/core-api/client", () => ({ signInAgain }));
vi.mock("next/cache", () => ({ refresh }));
const idle = { status: "idle" as const };
const form = (entries: Record<string, string>) => { const data = new FormData(); Object.entries(entries).forEach(([k,v]) => data.set(k,v)); return data; };
beforeEach(() => { vi.resetAllMocks(); catalogueRequest.mockResolvedValue(new Response("{}")); });

test("saves the feed address and distinguishes a suspicious import from success", async () => {
  catalogueRequest.mockResolvedValue(new Response(JSON.stringify({ source: {}, import: { id: "i1", status: "suspicious" } })));
  const result = await setFeed("b", idle, form({ url: "https://example.com/products.csv" }));
  expect(catalogueRequest).toHaveBeenCalledWith("b", "/feed", { method: "PUT", body: JSON.stringify({ url: "https://example.com/products.csv" }) });
  expect(result.status).toBe("error");
  expect(result.import?.id).toBe("i1");
  expect(refresh).toHaveBeenCalledOnce();
});
test("fixes only submitted fields and clears an individual fix with null", async () => {
  await fixProduct("b", "p1", idle, form({ title: "Trail shoe", price: "1499", currency: "INR", "clear:imageLink": "on" }));
  expect(catalogueRequest).toHaveBeenCalledWith("b", "/products/p1/fixes", { method: "PATCH", body: JSON.stringify({ title: "Trail shoe", price: 1499, currency: "INR", imageLink: null }) });
});
test.each(["abc", "-1", "Infinity"])("rejects invalid price %s without calling the API", async (price) => {
  expect((await fixProduct("b", "p", idle, form({ price, currency: "INR" }))).fieldErrors?.price).toBeTruthy();
  expect(catalogueRequest).not.toHaveBeenCalled();
});
test("empty optional inputs do not clear fixes and stock cannot be overridden", async () => {
  await fixProduct("b", "p", idle, form({ title: "", stock: "in_stock", availability: "in_stock", currency: "INR" }));
  expect(catalogueRequest).not.toHaveBeenCalled();
});
test("failures retain errors and do not refresh; expired sessions redirect", async () => {
  catalogueRequest.mockResolvedValueOnce(new Response(JSON.stringify({ errors: { url: ["Use a feed address."] } }), { status: 400 }));
  expect((await setFeed("b", idle, form({ url: "bad" }))).fieldErrors?.url).toEqual(["Use a feed address."]);
  catalogueRequest.mockRejectedValueOnce(new Error("offline"));
  expect((await refreshFeed("b", idle, new FormData())).message).toContain("try again");
  expect(refresh).not.toHaveBeenCalled();
  catalogueRequest.mockResolvedValueOnce(new Response(null, { status: 401 }));
  signInAgain.mockRejectedValueOnce(new Error("sign in"));
  await expect(stopFeed("b", idle, new FormData())).rejects.toThrow("sign in");
});
