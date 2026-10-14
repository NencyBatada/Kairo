import { beforeEach, expect, test, vi } from "vitest";

import { markNotificationRead } from "./actions";

const { coreApi, signInAgain, refresh, post } = vi.hoisted(() => ({
  coreApi: vi.fn(),
  signInAgain: vi.fn(),
  refresh: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));

beforeEach(() => {
  vi.clearAllMocks();
  coreApi.mockResolvedValue({ POST: post });
});

test("marks the notification read and refreshes so the unread count updates", async () => {
  post.mockResolvedValue({ response: { status: 204, ok: true } });

  await expect(markNotificationRead("brand-1", 9)).resolves.toEqual({ ok: true });

  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/notifications/{id}/read", {
    params: { path: { brandId: "brand-1", id: 9 } },
  });
  expect(refresh).toHaveBeenCalledOnce();
});

test("treats a notification that no longer exists as done", async () => {
  post.mockResolvedValue({ response: { status: 404, ok: false } });

  await expect(markNotificationRead("brand-1", 9)).resolves.toEqual({ ok: true });
});

test("reports a failure instead of throwing when the core refuses or cannot be reached", async () => {
  post.mockResolvedValueOnce({ response: { status: 500, ok: false } });
  await expect(markNotificationRead("brand-1", 9)).resolves.toEqual({ ok: false });

  post.mockRejectedValueOnce(new Error("fetch failed"));
  await expect(markNotificationRead("brand-1", 9)).resolves.toEqual({ ok: false });

  expect(refresh).not.toHaveBeenCalled();
});

test("returns to sign-in when the session has ended", async () => {
  post.mockResolvedValue({ response: { status: 401, ok: false } });
  signInAgain.mockImplementation(() => {
    throw new Error("sign-in redirect");
  });

  await expect(markNotificationRead("brand-1", 9)).rejects.toThrow("sign-in redirect");
});
