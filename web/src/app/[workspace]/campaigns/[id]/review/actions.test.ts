import { beforeEach, expect, test, vi } from "vitest";
import { reviewAssets } from "./actions";
import { brandId, campaignId, asset } from "../assets/fixtures.test-data";
import type { ReviewDecision } from "./_lib/review";
const { post, api, refresh, signInAgain } = vi.hoisted(() => ({ post: vi.fn(), api: vi.fn(), refresh: vi.fn(), signInAgain: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: api, signInAgain }));
vi.mock("next/cache", () => ({ refresh }));
beforeEach(() => { vi.clearAllMocks(); api.mockResolvedValue({ POST: post }); post.mockResolvedValue({ response: new Response(null, { status: 200 }) }); signInAgain.mockRejectedValue(new Error("sign-in")); });
test.each([['check', '/assets/check', { version: 4 }], ['request', '/approval-requests', { version: 4, gate: 'assets' }], ['approve', '/approve', { version: 4, gate: 'assets', comment: null }]] as const)("%s uses its scoped endpoint and original campaign version", async (kind, path, body) => {
  expect((await reviewAssets(brandId, campaignId, "4", { kind })).status).toBe("saved"); expect(post.mock.calls[0][0]).toBe(`/api/brands/{brandId}/campaigns/{campaignId}${path}`); expect(post.mock.calls[0][1]).toEqual({ params: { path: { brandId, campaignId } }, body }); expect(refresh).toHaveBeenCalledOnce();
});
test("override uses the asset version, and send-back uses the campaign version and chosen subset", async () => {
  await reviewAssets(brandId, campaignId, 4, { kind: "override", assetId: asset().id, assetVersion: 2, reason: "  Approved   exception " });
  expect(post.mock.calls[0][1]).toMatchObject({ params: { path: { brandId, campaignId, assetId: asset().id } }, body: { version: 2, reason: "Approved exception" } });
  await reviewAssets(brandId, campaignId, 4, { kind: "send-back", assetIds: [asset().id], reason: "  Fix   voice " }); expect(post.mock.calls[1][1].body).toEqual({ version: 4, assetIds: [asset().id], comment: "Fix voice" });
});
test("invalid versions and decisions never reach the API", async () => {
  for (const value of [0, -1, "1.5", "2147483648", "bad"]) expect((await reviewAssets(brandId, campaignId, value, { kind: "approve" })).status).toBe("error");
  for (const decision of [{ kind: "launch" }, null, { kind: "send-back", reason: "Fix this", assetIds: [] }, { kind: "send-back", reason: "Fix this", assetIds: [asset().id, asset().id] }, { kind: "override", reason: "ok", assetId: asset().id, assetVersion: 1 }, { kind: "override", reason: "Valid reason", assetId: "bad", assetVersion: 1 }, { kind: "override", reason: "Valid reason", assetId: asset().id, assetVersion: 0 }]) expect((await reviewAssets(brandId, campaignId, 4, decision as ReviewDecision)).status).toBe("error"); expect(api).not.toHaveBeenCalled();
});
test.each([400, 403, 404, 409, 503])("keeps API errors without refresh (%s)", async status => { post.mockResolvedValue({ response: new Response(null, { status }), error: { title: "Review changed", errors: status === 400 ? { comment: ["Explain the correction"] } : undefined } }); const result = await reviewAssets(brandId, campaignId, 4, { kind: "send-back", assetIds: [asset().id], reason: "Fix this" }); expect(result.status).toBe("error"); if (status === 400) expect(result.fieldErrors?.reason).toEqual(["Explain the correction"]); else expect(result.message).toBe("Review changed"); expect(result.conflict).toBe(status === 409); expect(refresh).not.toHaveBeenCalled(); });
test("offline failures retain comments and expired sessions follow sign-in", async () => { post.mockRejectedValueOnce(new Error("offline")); expect((await reviewAssets(brandId, campaignId, 4, { kind: "approve" })).message).toContain("still here"); post.mockResolvedValueOnce({ response: new Response(null, { status: 401 }) }); await expect(reviewAssets(brandId, campaignId, 4, { kind: "approve" })).rejects.toThrow("sign-in"); expect(refresh).not.toHaveBeenCalled(); });
