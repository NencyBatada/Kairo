import { beforeEach, expect, test, vi } from "vitest";

import { checkConnection, chooseConnectionAccount, disconnectConnection } from "./actions";

const { coreApi, signInAgain, refresh, post, del } = vi.hoisted(() => ({
  coreApi: vi.fn(),
  signInAgain: vi.fn(),
  refresh: vi.fn(),
  post: vi.fn(),
  del: vi.fn(),
}));

vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));

beforeEach(() => {
  vi.clearAllMocks();
  coreApi.mockResolvedValue({ POST: post, DELETE: del });
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

test("checks the scoped product and confirms access when the core reports no problem", async () => {
  post.mockResolvedValue({ data: { problem: null, connection: { state: "connected" } }, response: { status: 200, ok: true } });

  const result = await checkConnection("brand-1", "youtube", { status: "idle" }, new FormData());

  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/connections/{product}/check", {
    params: { path: { brandId: "brand-1", product: "youtube" } },
  });
  expect(result).toEqual({ status: "saved", message: "Access confirmed." });
  expect(refresh).toHaveBeenCalledOnce();
});

test("shows the core's access problem after a successful check", async () => {
  post.mockResolvedValue({ data: { problem: "Account access expired.", connection: { state: "needsReconnect" } }, response: { status: 200, ok: true } });

  const result = await checkConnection("brand-1", "youtube", { status: "idle" }, new FormData());

  expect(result).toEqual({ status: "error", message: "Account access expired." });
  expect(refresh).toHaveBeenCalledOnce();
});

test("keeps failed access checks visible", async () => {
  post.mockResolvedValue({ error: { detail: "Google is unavailable." }, response: { status: 503, ok: false } });

  const result = await checkConnection("brand-1", "youtube", { status: "idle" }, new FormData());

  expect(result).toEqual({ status: "error", message: "Google is unavailable." });
  expect(refresh).not.toHaveBeenCalled();
});

test("disconnects the scoped product and refreshes the page", async () => {
  del.mockResolvedValue({ response: { status: 204, ok: true } });

  const result = await disconnectConnection("brand-1", "youtube", { status: "idle" }, new FormData());

  expect(del).toHaveBeenCalledWith("/api/brands/{brandId}/connections/{product}", {
    params: { path: { brandId: "brand-1", product: "youtube" } },
  });
  expect(result).toEqual({ status: "saved", message: "Connection removed." });
  expect(refresh).toHaveBeenCalledOnce();
});

test.each(["check", "disconnect"])("returns to sign-in when %s loses the session", async (operation) => {
  const response = { response: { status: 401, ok: false } };
  post.mockResolvedValue(response);
  del.mockResolvedValue(response);
  signInAgain.mockImplementation(() => { throw new Error("sign-in redirect"); });
  const action = operation === "check" ? checkConnection : disconnectConnection;

  await expect(action("brand-1", "youtube", { status: "idle" }, new FormData())).rejects.toThrow("sign-in redirect");
  expect(refresh).not.toHaveBeenCalled();
});

test("reports a failed disconnect without refreshing", async () => {
  del.mockResolvedValue({ error: { detail: "Disconnect failed." }, response: { status: 503, ok: false } });

  const result = await disconnectConnection("brand-1", "youtube", { status: "idle" }, new FormData());

  expect(result).toEqual({ status: "error", message: "Disconnect failed." });
  expect(refresh).not.toHaveBeenCalled();
});

test.each(["choose", "check", "disconnect"])("keeps a rejected %s request as a recoverable row error", async (operation) => {
  post.mockRejectedValue(new Error("fetch failed"));
  del.mockRejectedValue(new Error("fetch failed"));
  const action = operation === "choose" ? chooseConnectionAccount : operation === "check" ? checkConnection : disconnectConnection;

  const result = await action("brand-1", "youtube", { status: "idle" }, new FormData());

  expect(result).toEqual({ status: "error", message: "Could not reach the service. Please try again." });
  expect(refresh).not.toHaveBeenCalled();
  expect(signInAgain).not.toHaveBeenCalled();
});
