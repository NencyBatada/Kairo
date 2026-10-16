import { beforeEach, expect, test, vi } from "vitest";
import { changeLockedPlan, chooseHoldouts } from "./proof-actions";

const { post, coreApi, refresh, signInAgain } = vi.hoisted(() => ({ post: vi.fn(), coreApi: vi.fn(), refresh: vi.fn(), signInAgain: vi.fn() }));
vi.mock("next/cache", () => ({ refresh }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
beforeEach(() => { vi.clearAllMocks(); coreApi.mockResolvedValue({ POST: post }); post.mockResolvedValue({ response: new Response(null, { status: 200 }) }); signInAgain.mockRejectedValue(new Error("sign-in")); });
function form(ids = ["pune"], extra: Record<string, string> = {}) { const form = new FormData(); form.set("version", "3"); for (const id of ids) form.append("regionIds", id); for (const [name, value] of Object.entries(extra)) form.set(name, value); return form; }
test("sends unique selected holdouts and original version, then refreshes", async () => {
  expect((await chooseHoldouts("b", "c", form([" pune ", "pune"]))).status).toBe("saved"); expect(post.mock.calls[0]).toEqual(["/api/brands/{brandId}/campaigns/{campaignId}/proof-plan/holdouts", { params: { path: { brandId: "b", campaignId: "c" } }, body: { version: 3, regionIds: ["pune"] } }]); expect(refresh).toHaveBeenCalledOnce();
});
test.each([{ ids: [] }, { ids: Array.from({ length: 11 }, (_, i) => `r${i}`) }])("rejects invalid holdout counts", async ({ ids }) => { expect((await chooseHoldouts("b", "c", form(ids))).fieldErrors?.regionIds).toBeTruthy(); expect(post).not.toHaveBeenCalled(); });
test("locked changes carry the recorded reason, holdouts and end date", async () => {
  expect((await changeLockedPlan("b", "c", form(["pune"], { reason: "Market closure", endDate: "2026-11-02" }))).status).toBe("saved"); expect(post.mock.calls[0][1].body).toEqual({ version: 3, reason: "Market closure", holdoutRegionIds: ["pune"], endDate: "2026-11-02" });
});
test("requires reason and real dates, without sending invalid requests", async () => {
  const result = await changeLockedPlan("b", "c", form(["pune"], { reason: " ", endDate: "2026-02-30" })); expect(result.fieldErrors).toHaveProperty("reason"); expect(result.fieldErrors).toHaveProperty("endDate"); expect(post).not.toHaveBeenCalled();
});
test.each([403, 409, 502])("passes service errors (%s) without refreshing", async status => {
  post.mockResolvedValue({ response: new Response(null, { status }), error: { title: "This campaign changed. Reload it." } }); expect((await chooseHoldouts("b", "c", form())).message).toMatch(/changed/); expect(refresh).not.toHaveBeenCalled();
});
test("maps field errors, handles offline requests and redirects expired sessions", async () => {
  post.mockResolvedValueOnce({ response: new Response(null, { status: 400 }), error: { errors: { regionIds: ["Leave a test region"] } } }); expect((await chooseHoldouts("b", "c", form())).fieldErrors?.regionIds).toEqual(["Leave a test region"]);
  post.mockRejectedValueOnce(new Error("offline")); expect((await changeLockedPlan("b", "c", form(["pune"], { reason: "Market closure" }))).message).toMatch(/choices are still here/);
  post.mockResolvedValueOnce({ response: new Response(null, { status: 401 }) }); await expect(chooseHoldouts("b", "c", form())).rejects.toThrow("sign-in");
});
