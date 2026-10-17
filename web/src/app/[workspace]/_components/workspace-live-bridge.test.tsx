import { beforeEach, expect, test, vi } from "vitest";
import { workspace } from "@/lib/ask-kairo/fixtures.test-data";
import { WorkspaceLiveBridge } from "./workspace-live-bridge";
const get = vi.hoisted(() => vi.fn());
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace: vi.fn(async () => workspace), getMe: vi.fn(async () => ({ id: "current-user" })) }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: async () => ({ GET: get }), unwrap: async (result: { data: unknown }) => result.data }));
beforeEach(() => get.mockResolvedValue({ data: { ...workspace, timeZone: "Asia/Kolkata", currency: "INR" } }));
test("the live bridge supplies current workspace roles, time zone and account identity", async () => { const node = await WorkspaceLiveBridge({ params: Promise.resolve({ workspace: "acme" }) }); expect(node.props.workspace).toEqual({ ...workspace, timeZone: "Asia/Kolkata", currency: "INR", userId: "current-user" }); expect(get).toHaveBeenCalledWith("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }); });
