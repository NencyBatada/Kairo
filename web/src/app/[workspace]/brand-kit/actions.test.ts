import { beforeEach, expect, test, vi } from "vitest";

import { approveKit, discardKit, draftKit, removeKitFile, restoreKit, saveKit } from "./actions";

const { coreApi, signInAgain, refresh, put, post, del } = vi.hoisted(() => ({
  coreApi: vi.fn(), signInAgain: vi.fn(), refresh: vi.fn(), put: vi.fn(), post: vi.fn(), del: vi.fn(),
}));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));

const idle = { status: "idle" as const };
const saved = { number: 3, revision: 7, content: { voice: { summary: "Warm" } } };
function form(values: Record<string, string>) {
  const data = new FormData();
  Object.entries(values).forEach(([key, value]) => data.set(key, value));
  return data;
}

beforeEach(() => {
  vi.resetAllMocks();
  coreApi.mockResolvedValue({ PUT: put, POST: post, DELETE: del });
  for (const method of [put, post, del]) method.mockResolvedValue({ data: saved, response: { ok: true, status: 200 } });
});

test("saves the entire kit with the revision the editor started from", async () => {
  const content = { voice: { summary: "Edited", sources: [{ kind: "page", ref: "https://brand.example" }] }, guidelineFileIds: ["file-1"] };
  const result = await saveKit("brand-1", idle, form({ content: JSON.stringify(content), revision: "6" }));
  expect(put).toHaveBeenCalledWith("/api/brands/{brandId}/brand-kit/draft", {
    params: { path: { brandId: "brand-1" } }, body: { content, revision: 6 },
  });
  expect(result.version).toEqual(saved);
  expect(refresh).toHaveBeenCalledOnce();
});

test.each(["bad", "-1", "1.5", "", "Infinity"])("rejects invalid revision %s without sending a mutation", async (revision) => {
  expect((await approveKit("brand-1", idle, form({ revision }))).status).toBe("error");
  expect(post).not.toHaveBeenCalled();
});

test("rejects malformed content without losing the server draft", async () => {
  expect((await saveKit("b", idle, form({ content: "{", revision: "0" }))).status).toBe("error");
  expect(put).not.toHaveBeenCalled();
});

test("approves the displayed revision and restores into the current draft revision", async () => {
  await approveKit("b", idle, form({ revision: "7" }));
  expect(post).toHaveBeenLastCalledWith("/api/brands/{brandId}/brand-kit/draft/approve", {
    params: { path: { brandId: "b" } }, body: { revision: 7 },
  });
  await restoreKit("b", 2, idle, form({ revision: "9" }));
  expect(post).toHaveBeenLastCalledWith("/api/brands/{brandId}/brand-kit/versions/{number}/restore", {
    params: { path: { brandId: "b", number: 2 } }, body: { revision: 9 },
  });
});

test("replacement consent is explicit when starting a drafting run", async () => {
  await draftKit("b", idle, form({ websiteUrl: "https://brand.example", replaceDraft: "on" }));
  expect(post).toHaveBeenLastCalledWith("/api/brands/{brandId}/brand-kit/drafting", {
    params: { path: { brandId: "b" } }, body: { websiteUrl: "https://brand.example", replaceDraft: true },
  });
  await draftKit("b", idle, form({ websiteUrl: "https://brand.example" }));
  expect(post.mock.lastCall?.[1].body.replaceDraft).toBe(false);
});

test("reports conflicts and permission failures without refreshing away local input", async () => {
  post.mockResolvedValueOnce({ error: { title: "This draft changed. Reload to review it." }, response: { ok: false, status: 409 } });
  expect(await approveKit("b", idle, form({ revision: "7" }))).toMatchObject({ status: "error", message: "This draft changed. Reload to review it." });
  del.mockResolvedValueOnce({ response: { ok: false, status: 403 } });
  expect((await discardKit("b", idle, new FormData())).message).toContain("permission");
  expect(refresh).not.toHaveBeenCalled();
});

test("keeps field validation errors and reports transport errors inline", async () => {
  put.mockResolvedValueOnce({ error: { errors: { "content.colours[0].hex": ["Use a colour code."] } }, response: { ok: false, status: 400 } });
  expect((await saveKit("b", idle, form({ content: "{}", revision: "1" }))).fieldErrors).toEqual({ "content.colours[0].hex": ["Use a colour code."] });
  post.mockRejectedValueOnce(new Error("network"));
  expect((await approveKit("b", idle, form({ revision: "1" }))).message).toContain("try again");
  expect(refresh).not.toHaveBeenCalled();
});

test("redirects expired sessions and removes files through the scoped endpoint", async () => {
  await removeKitFile("b", "file-1", idle, new FormData());
  expect(del).toHaveBeenLastCalledWith("/api/brands/{brandId}/brand-kit/files/{fileId}", { params: { path: { brandId: "b", fileId: "file-1" } } });
  post.mockResolvedValueOnce({ response: { ok: false, status: 401 } });
  signInAgain.mockRejectedValueOnce(new Error("sign in"));
  await expect(approveKit("b", idle, form({ revision: "1" }))).rejects.toThrow("sign in");
});
