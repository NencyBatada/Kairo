import { beforeEach, expect, test, vi } from "vitest";

import { idle } from "@/lib/forms";
import { retryRun } from "./actions";

const { coreApi, post, refresh, signInAgain } = vi.hoisted(() => ({ coreApi: vi.fn(), post: vi.fn(), refresh: vi.fn(), signInAgain: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));
const retry = () => retryRun("brand-1", "room-1", "run-1", idle, new FormData());
beforeEach(() => { vi.clearAllMocks(); coreApi.mockResolvedValue({ POST: post }); });

test("retries the exact room and run, then refreshes live data", async () => {
  post.mockResolvedValue({ response: new Response(null, { status: 200 }) });
  expect(await retry()).toEqual({ status: "saved", message: "The agent run is queued again." });
  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/campaigns/{campaignId}/stage-runs/{runId}/retry", { params: { path: { brandId: "brand-1", campaignId: "room-1", runId: "run-1" } } });
  expect(refresh).toHaveBeenCalledOnce();
});

test.each([403, 404, 409, 503])("reports failed retries without refreshing (%i)", async (status) => {
  post.mockResolvedValue({ response: new Response(null, { status }), error: { detail: "This run cannot be retried." } });
  expect(await retry()).toMatchObject({ status: "error", message: "This run cannot be retried." });
  expect(refresh).not.toHaveBeenCalled();
});

test("handles network failure, validation and expired sessions", async () => {
  post.mockRejectedValueOnce(new Error("network"));
  expect(await retry()).toMatchObject({ status: "error", message: expect.stringContaining("reach the service") });
  post.mockResolvedValueOnce({ response: new Response(null, { status: 400 }), error: { errors: { runId: ["Choose a failed run."] } } });
  expect(await retry()).toMatchObject({ status: "error", message: "Choose a failed run." });
  post.mockResolvedValueOnce({ response: new Response(null, { status: 401 }) });
  await retry(); expect(signInAgain).toHaveBeenCalledOnce();
});
