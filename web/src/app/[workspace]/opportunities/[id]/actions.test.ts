import { beforeEach, expect, test, vi } from "vitest";

import { acceptOpportunity, dismissOpportunity, saveOpportunity, unsaveOpportunity } from "./actions";

const { coreApi, post, refresh, redirect, signInAgain } = vi.hoisted(() => ({ coreApi: vi.fn(), post: vi.fn(), refresh: vi.fn(), redirect: vi.fn(), signInAgain: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));
vi.mock("next/navigation", () => ({ redirect }));
beforeEach(() => {
  vi.clearAllMocks();
  coreApi.mockResolvedValue({ POST: post });
  post.mockResolvedValue({ data: { status: "saved" }, response: { status: 200, ok: true } });
  redirect.mockImplementation((path) => { throw new Error(`redirect:${path}`); });
  signInAgain.mockImplementation(() => { throw new Error("sign-in"); });
});
const idle = { status: "idle" } as const;

test("accept redirects to the room returned by the API", async () => {
  post.mockResolvedValue({ data: { roomId: "room-1" }, response: { status: 200, ok: true } });
  await expect(acceptOpportunity("brand-1", "opp-1", "acme", idle, new FormData())).rejects.toThrow("redirect:/acme/campaigns/room-1");
  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities/{opportunityId}/accept", { params: { path: { brandId: "brand-1", opportunityId: "opp-1" } } });
});

test("does not redirect when stock disappears or an opportunity has already closed", async () => {
  post.mockResolvedValue({ error: { title: "None of the matching products is in stock." }, response: { status: 409, ok: false } });
  expect(await acceptOpportunity("brand-1", "opp-1", "acme", idle, new FormData())).toMatchObject({ status: "error", message: "None of the matching products is in stock." });
  expect(redirect).not.toHaveBeenCalled();
  expect(refresh).not.toHaveBeenCalled();
});

test("reports an unreadable successful accept result without creating a second room", async () => {
  expect(await acceptOpportunity("brand-1", "opp-1", "acme", idle, new FormData())).toMatchObject({ status: "error" });
  expect(post).toHaveBeenCalledTimes(1);
  expect(redirect).not.toHaveBeenCalled();
});

test("save and unsave use separate endpoints and refresh the page", async () => {
  expect(await saveOpportunity("brand-1", "opp-1", idle, new FormData())).toMatchObject({ status: "saved", opportunityStatus: "saved" });
  expect(await unsaveOpportunity("brand-1", "opp-1", idle, new FormData())).toMatchObject({ status: "saved", opportunityStatus: "new" });
  expect(post.mock.calls.map(([path]) => path)).toEqual(["/api/brands/{brandId}/opportunities/{opportunityId}/save", "/api/brands/{brandId}/opportunities/{opportunityId}/unsave"]);
  expect(refresh).toHaveBeenCalledTimes(2);
});

test.each([["", ""], ["invalid", ""], ["other", ""], ["other", "x".repeat(501)]])("validates dismiss reason %s and note before sending", async (reason, note) => {
  const data = new FormData(); data.set("reason", reason); data.set("note", note);
  expect((await dismissOpportunity("brand-1", "opp-1", idle, data)).status).toBe("error");
  expect(post).not.toHaveBeenCalled();
});

test("dismiss sends the chosen reason and a trimmed note", async () => {
  const data = new FormData(); data.set("reason", "other"); data.set("note", " Already planned ");
  expect(await dismissOpportunity("brand-1", "opp-1", idle, data)).toMatchObject({ status: "saved", opportunityStatus: "dismissed" });
  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities/{opportunityId}/dismiss", { params: { path: { brandId: "brand-1", opportunityId: "opp-1" } }, body: { reason: "other", note: "Already planned" } });
});

test("preserves field validation from the service", async () => {
  post.mockResolvedValue({ response: { status: 400, ok: false }, error: { errors: { note: ["A note is required."] } } });
  expect(await saveOpportunity("brand-1", "opp-1", idle, new FormData())).toMatchObject({ fieldErrors: { note: ["A note is required."] } });
});

test("handles network failures and expired sessions", async () => {
  post.mockRejectedValueOnce(new Error("offline"));
  expect(await saveOpportunity("brand-1", "opp-1", idle, new FormData())).toMatchObject({ status: "error", message: expect.stringContaining("couldn't reach") });
  post.mockResolvedValue({ response: { status: 401, ok: false } });
  await expect(saveOpportunity("brand-1", "opp-1", idle, new FormData())).rejects.toThrow("sign-in");
});

test.each([[403, "permission"], [404, "no longer exists"]])("handles a %s response inline", async (status, message) => {
  post.mockResolvedValue({ response: { status, ok: false } });
  expect(await saveOpportunity("brand-1", "opp-1", idle, new FormData())).toMatchObject({ status: "error", message: expect.stringContaining(message) });
});
