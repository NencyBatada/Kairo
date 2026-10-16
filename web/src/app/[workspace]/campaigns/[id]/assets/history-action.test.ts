import { beforeEach, expect, test, vi } from "vitest";
import { loadAssetHistory } from "./history-action";
import { history } from "./fixtures.test-data";
const { get, signIn } = vi.hoisted(() => ({ get: vi.fn(), signIn: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: async () => ({ GET: get }), signInAgain: signIn }));
beforeEach(() => { vi.clearAllMocks(); });
test("reads immutable versions in campaign scope", async () => { get.mockResolvedValue({ response: { status: 200, ok: true }, data: history() }); expect((await loadAssetHistory("b", "c", "a")).history).toEqual(history()); expect(get.mock.calls[0][1].params.path).toEqual({ brandId: "b", campaignId: "c", assetId: "a" }); });
test("handles failed and missing reads", async () => { get.mockRejectedValueOnce(new Error("offline")); expect((await loadAssetHistory("b", "c", "a")).error).toContain("Try again"); get.mockResolvedValue({ response: { status: 404, ok: false } }); expect((await loadAssetHistory("b", "c", "a")).error).toContain("no longer available"); });
test("expired sessions use the established sign-in flow", async () => { get.mockResolvedValue({ response: { status: 401, ok: false } }); await loadAssetHistory("b", "c", "a"); expect(signIn).toHaveBeenCalled(); });
