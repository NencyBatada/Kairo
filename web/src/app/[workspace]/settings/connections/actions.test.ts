import { beforeEach, expect, test, vi } from "vitest";

import { chooseConnectionAccount } from "./actions";

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

test("posts the chosen external account to the scoped product and refreshes", async () => {
  post.mockResolvedValue({ data: { state: "connected" }, response: { status: 200, ok: true } });
  const formData = new FormData();
  formData.set("externalId", " 123 ");

  const result = await chooseConnectionAccount("brand-1", "google-ads", { status: "idle" }, formData);

  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/connections/{product}/account", {
    params: { path: { brandId: "brand-1", product: "google-ads" } },
    body: { externalId: "123" },
  });
  expect(result).toEqual({ status: "saved", message: "Account connected." });
  expect(refresh).toHaveBeenCalledOnce();
});

test("returns the core field error when the account cannot be selected", async () => {
  post.mockResolvedValue({ error: { errors: { externalId: ["Choose an account."] } }, response: { status: 400, ok: false } });
  const result = await chooseConnectionAccount("brand-1", "google-ads", { status: "idle" }, new FormData());
  expect(result).toEqual({ status: "error", fieldErrors: { externalId: ["Choose an account."] } });
  expect(refresh).not.toHaveBeenCalled();
});

test("returns to sign-in when account selection loses the session", async () => {
  post.mockResolvedValue({ response: { status: 401, ok: false } });
  signInAgain.mockImplementation(() => { throw new Error("sign-in redirect"); });
  await expect(chooseConnectionAccount("brand-1", "google-ads", { status: "idle" }, new FormData())).rejects.toThrow("sign-in redirect");
});
