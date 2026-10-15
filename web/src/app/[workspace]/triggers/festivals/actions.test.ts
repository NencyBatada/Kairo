import { beforeEach, expect, test, vi } from "vitest";
import { removeFestival, saveFestival } from "./actions";
import { validDate } from "./_lib/calendar";

const { coreApi, post, put, remove, refresh, revalidatePath, redirect, signInAgain } = vi.hoisted(() => ({ coreApi: vi.fn(), post: vi.fn(), put: vi.fn(), remove: vi.fn(), refresh: vi.fn(), revalidatePath: vi.fn(), redirect: vi.fn(), signInAgain: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh, revalidatePath }));
vi.mock("next/navigation", () => ({ redirect }));
const idle = { status: "idle" } as const;
const form = (start = "2026-11-08", end = "") => { const data = new FormData(); data.set("name", " Diwali   weekend "); data.set("startsOn", start); data.set("endsOn", end); data.append("regions", "IN-MH"); return data; };
beforeEach(() => {
  vi.clearAllMocks(); coreApi.mockResolvedValue({ POST: post, PUT: put, DELETE: remove });
  for (const request of [post, put, remove]) request.mockResolvedValue({ response: { status: 200, ok: true } });
  redirect.mockImplementation(() => { throw new Error("redirect"); });
  signInAgain.mockImplementation(() => { throw new Error("sign-in"); });
});
test.each([null, "event-1"])("saves a normalised event in the current brand (%s)", async (id) => {
  expect((await saveFestival("brand-1", id, idle, form())).status).toBe("saved");
  expect(id ? put : post).toHaveBeenCalledWith(id ? "/api/brands/{brandId}/triggers/festivals/{festivalId}" : "/api/brands/{brandId}/triggers/festivals", { params: { path: id ? { brandId: "brand-1", festivalId: id } : { brandId: "brand-1" } }, body: { name: "Diwali weekend", startsOn: "2026-11-08", endsOn: "2026-11-08", regions: ["IN-MH"] } });
  expect(refresh).toHaveBeenCalledOnce();
});
test.each([["2026-02-30", "2026-03-01"], ["", ""], ["2026-11-08", "2026-11-07"], ["2026-11-01", "2026-11-15"], ["2026-11-08", "bad"]])("rejects invalid or overlong ranges %s to %s", async (start, end) => {
  expect((await saveFestival("brand-1", null, idle, form(start, end))).status).toBe("error"); expect(post).not.toHaveBeenCalled();
});
test("accepts fourteen days including both endpoints and all-India scope", async () => {
  const data = form("2026-11-01", "2026-11-14"); data.delete("regions");
  expect((await saveFestival("brand-1", null, idle, data)).status).toBe("saved");
  expect(post.mock.calls[0][1].body.regions).toEqual([]);
  expect(validDate("2028-02-29")).toBe(true); expect(validDate("2026-02-29")).toBe(false);
});
test("removal revalidates and returns to the calendar", async () => {
  await expect(removeFestival("brand-1", "event-1", "acme", idle, new FormData())).rejects.toThrow("redirect");
  expect(revalidatePath).toHaveBeenCalledWith("/acme/triggers/festivals"); expect(redirect).toHaveBeenCalledWith("/acme/triggers/festivals");
  expect(refresh).not.toHaveBeenCalled();
});

test("preserves in-use conflicts on removal", async () => {
  remove.mockResolvedValue({ response: { status: 409, ok: false }, error: { title: "A trigger rule uses this event. Change or delete the rule first." } });
  expect(await removeFestival("brand-1", "event-1", "acme", idle, new FormData())).toMatchObject({ status: "error", message: expect.stringContaining("trigger rule") });
  expect(remove).toHaveBeenCalledWith("/api/brands/{brandId}/triggers/festivals/{festivalId}", { params: { path: { brandId: "brand-1", festivalId: "event-1" } } });
  expect(refresh).not.toHaveBeenCalled();
});
test.each([400, 403, 404, 409])("shows service failures (%s) without reporting success", async (status) => {
  put.mockResolvedValue({ response: { status, ok: false }, error: { errors: { regions: ["Choose states from the list."] } } });
  expect(await saveFestival("brand-1", "event-1", idle, form())).toMatchObject({ status: "error", fieldErrors: { regions: ["Choose states from the list."] } });
  expect(refresh).not.toHaveBeenCalled();
});
test("redirects expired sessions and returns network errors inline", async () => {
  post.mockResolvedValue({ response: { status: 401, ok: false } }); remove.mockResolvedValue({ response: { status: 401, ok: false } });
  await expect(saveFestival("brand-1", null, idle, form())).rejects.toThrow("sign-in");
  await expect(removeFestival("brand-1", "event-1", "acme", idle, new FormData())).rejects.toThrow("sign-in");
  post.mockRejectedValue(new Error("offline"));
  expect(await saveFestival("brand-1", null, idle, form())).toMatchObject({ status: "error", message: expect.stringContaining("couldn't reach") });
});
