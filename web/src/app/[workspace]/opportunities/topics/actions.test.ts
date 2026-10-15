import { beforeEach, expect, test, vi } from "vitest";

import { addTopic, removeTopic } from "./actions";

const { coreApi, post, remove, refresh, signInAgain } = vi.hoisted(() => ({ coreApi: vi.fn(), post: vi.fn(), remove: vi.fn(), refresh: vi.fn(), signInAgain: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));
beforeEach(() => {
  vi.clearAllMocks();
  coreApi.mockResolvedValue({ POST: post, DELETE: remove });
  post.mockResolvedValue({ response: { status: 201, ok: true } });
  remove.mockResolvedValue({ response: { status: 204, ok: true } });
  signInAgain.mockImplementation(() => { throw new Error("sign-in"); });
});
const idle = { status: "idle" } as const;
const form = (phrase: string, kind = "watch") => { const data = new FormData(); data.set("phrase", phrase); data.set("kind", kind); return data; };

test("normalises whitespace and sends the selected preference in the current brand", async () => {
  expect(await addTopic("brand-1", idle, form(" waterproof   trainers ", "ignore"))).toMatchObject({ status: "saved", message: "Topic added to Ignored." });
  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities/topics", { params: { path: { brandId: "brand-1" } }, body: { phrase: "waterproof trainers", kind: "ignore" } });
  expect(refresh).toHaveBeenCalledOnce();
});

test.each([["a", "watch"], ["x".repeat(101), "watch"], ["rain", "bad"], ["  ", "ignore"]])("rejects an invalid phrase or preference before writing", async (phrase, kind) => {
  expect((await addTopic("brand-1", idle, form(phrase, kind))).status).toBe("error");
  expect(post).not.toHaveBeenCalled();
});

test.each(["That topic is already on your list.", "You can keep up to 200 topics."])("preserves a service conflict: %s", async (message) => {
  post.mockResolvedValue({ response: { status: 409, ok: false }, error: { title: message } });
  expect(await addTopic("brand-1", idle, form("trainers"))).toEqual({ status: "error", message });
  expect(refresh).not.toHaveBeenCalled();
});

test("removes only the requested topic in the current workspace", async () => {
  expect(await removeTopic("brand-1", "topic-1", idle, new FormData())).toEqual({ status: "saved", message: "Topic removed." });
  expect(remove).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities/topics/{topicId}", { params: { path: { brandId: "brand-1", topicId: "topic-1" } } });
});

test("handles an already removed topic without reporting success", async () => {
  remove.mockResolvedValue({ response: { status: 404, ok: false } });
  expect(await removeTopic("brand-1", "topic-1", idle, new FormData())).toEqual({ status: "error", message: "That no longer exists." });
  expect(refresh).not.toHaveBeenCalled();
});

test("redirects expired sessions for adds and removes", async () => {
  post.mockResolvedValue({ response: { status: 401, ok: false } });
  remove.mockResolvedValue({ response: { status: 401, ok: false } });
  await expect(addTopic("brand-1", idle, form("trainers"))).rejects.toThrow("sign-in");
  await expect(removeTopic("brand-1", "topic-1", idle, new FormData())).rejects.toThrow("sign-in");
});

test("returns permission and network failures inline", async () => {
  post.mockResolvedValue({ response: { status: 403, ok: false } });
  expect(await addTopic("brand-1", idle, form("trainers"))).toMatchObject({ status: "error", message: expect.stringContaining("permission") });
  remove.mockRejectedValue(new Error("offline"));
  expect(await removeTopic("brand-1", "topic-1", idle, new FormData())).toMatchObject({ status: "error", message: expect.stringContaining("couldn't reach") });
});
