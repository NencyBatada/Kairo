import { beforeEach, expect, test, vi } from "vitest";
import { detail, editForm, proposal } from "./fixtures.test-data";
import { applyBrief, decideBrief, proposeBrief, requestBriefApproval, saveBrief } from "./actions";

const { api, post, patch, refresh, signInAgain } = vi.hoisted(() => ({ api: vi.fn(), post: vi.fn(), patch: vi.fn(), refresh: vi.fn(), signInAgain: vi.fn() }));
vi.mock("next/cache", () => ({ refresh }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: api, signInAgain }));
beforeEach(() => { vi.clearAllMocks(); api.mockResolvedValue({ POST: post, PATCH: patch }); patch.mockResolvedValue({ response: new Response(null, { status: 200 }), data: detail }); post.mockResolvedValue({ response: new Response(null, { status: 200 }), data: proposal }); signInAgain.mockRejectedValue(new Error("sign-in")); });

test("saves the complete typed edit with both versions, then refreshes", async () => {
  const result = await saveBrief("brand", "campaign", editForm());
  expect(result.detail).toEqual(detail); expect(patch.mock.calls[0][1]).toMatchObject({ params: { path: { brandId: "brand", campaignId: "campaign" } }, body: { version: 1, briefVersion: 2, budget: "50000", productIds: ["p1"] } }); expect(refresh).toHaveBeenCalledOnce();
});
test("invalid edits never call the backend", async () => { expect((await saveBrief("b", "c", editForm({ budget: "0" }))).fieldErrors?.budget).toBeTruthy(); expect(api).not.toHaveBeenCalled(); });
test("instruction preview never refreshes or applies automatically", async () => {
  const form = editForm(); form.set("instruction", " Add Hindi ");
  expect((await proposeBrief("b", "c", form)).proposal).toEqual(proposal);
  expect(post.mock.calls[0][1].body).toEqual({ version: 1, briefVersion: 2, instruction: "Add Hindi" }); expect(refresh).not.toHaveBeenCalled();
});
test("validates instructions and proposal identifiers", async () => {
  expect((await proposeBrief("b", "c", editForm())).fieldErrors?.instruction).toBeTruthy();
  expect((await applyBrief("b", "c", "bad", "1")).status).toBe("error"); expect(api).not.toHaveBeenCalled();
});
test("apply sends only the proposal id and original campaign version", async () => {
  post.mockResolvedValue({ response: new Response(null, { status: 200 }), data: detail });
  expect((await applyBrief("b", "c", proposal.id, "1")).detail).toEqual(detail);
  expect(post.mock.calls[0]).toEqual(["/api/brands/{brandId}/campaigns/{campaignId}/brief/proposals/{proposalId}/apply", { params: { path: { brandId: "b", campaignId: "c", proposalId: proposal.id } }, body: { version: 1 } }]);
});
test.each([403, 409, 502])("preserves service errors (%s), without refreshing", async status => {
  patch.mockResolvedValue({ response: new Response(null, { status }), error: { title: "Reload before saving." } });
  expect((await saveBrief("b", "c", editForm())).message).toBe("Reload before saving."); expect(refresh).not.toHaveBeenCalled();
});
test("keeps field errors and explains network failures", async () => {
  patch.mockResolvedValueOnce({ response: new Response(null, { status: 400 }), error: { errors: { regions: ["Unknown region"] } } });
  expect((await saveBrief("b", "c", editForm())).fieldErrors).toEqual({ regions: ["Unknown region"] });
  patch.mockRejectedValueOnce(new Error("offline")); expect((await saveBrief("b", "c", editForm())).message).toMatch(/edits are still here/);
});
test("expired sessions return to sign-in", async () => { patch.mockResolvedValue({ response: new Response(null, { status: 401 }) }); await expect(saveBrief("b", "c", editForm())).rejects.toThrow("sign-in"); });
test("approval request uses gate 1 and the campaign version", async () => {
  await requestBriefApproval("b", "c", "4"); expect(post.mock.calls[0]).toEqual(["/api/brands/{brandId}/campaigns/{campaignId}/approval-requests", { params: { path: { brandId: "b", campaignId: "c" } }, body: { version: 4, gate: "brief" } }]);
});
test("approve and send-back are limited to the brief gate", async () => {
  await decideBrief("b", "c", "4", { kind: "approve" }); expect(post.mock.calls[0][1].body).toEqual({ gate: "brief", version: 4, comment: null });
  await decideBrief("b", "c", "4", { kind: "send-back", reason: "  Fix the dates  " }); expect(post.mock.calls[1][1].body).toEqual({ to: "brief_draft", version: 4, comment: "Fix the dates" });
});
test("requires a send-back comment and maps backend comment errors into the confirmation", async () => {
  expect((await decideBrief("b", "c", "4", { kind: "send-back", reason: " " })).fieldErrors?.reason).toBeTruthy(); expect(post).not.toHaveBeenCalled();
  post.mockResolvedValue({ response: new Response(null, { status: 400 }), error: { errors: { comment: ["Explain the problem"] } } });
  expect((await decideBrief("b", "c", "4", { kind: "send-back", reason: "Fix dates" })).fieldErrors?.reason).toEqual(["Explain the problem"]);
});
