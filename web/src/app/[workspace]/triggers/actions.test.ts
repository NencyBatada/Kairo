import { beforeEach, expect, test, vi } from "vitest";
import { changeRule, previewOrSaveRule } from "./actions";
import { readRule } from "./_lib/rules";

const { coreApi, post, put, remove, refresh, revalidatePath, redirect, signInAgain } = vi.hoisted(() => ({ coreApi: vi.fn(), post: vi.fn(), put: vi.fn(), remove: vi.fn(), refresh: vi.fn(), revalidatePath: vi.fn(), redirect: vi.fn(), signInAgain: vi.fn() }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain }));
vi.mock("next/cache", () => ({ refresh, revalidatePath }));
vi.mock("next/navigation", () => ({ redirect }));
const idle = { status: "idle" } as const;
const form = (source = "weather") => {
  const data = new FormData();
  Object.entries({ name: "Rain rule", source, category: "Trainers", cooldownHours: "72", "condition.kind": "heavy_rain", "condition.threshold": "50", "condition.withinDays": "3", "condition.eventId": "festival-1", "condition.daysBefore": "7", "condition.minUnits": "100" }).forEach(([key, value]) => data.set(key, value));
  data.append("regions", "mumbai");
  return data;
};
beforeEach(() => {
  vi.clearAllMocks();
  coreApi.mockResolvedValue({ POST: post, PUT: put, DELETE: remove });
  post.mockResolvedValue({ data: { description: "Plain words from the service." }, response: { status: 200, ok: true } });
  put.mockResolvedValue({ data: {}, response: { status: 200, ok: true } });
  remove.mockResolvedValue({ response: { status: 204, ok: true } });
  redirect.mockImplementation(() => { throw new Error("redirect"); });
  signInAgain.mockImplementation(() => { throw new Error("sign-in"); });
});

test.each(["weather", "festival", "stock"])("previews %s conditions with unrelated fields cleared", async (source) => {
  const result = await previewOrSaveRule("brand-1", null, idle, form(source));
  expect(result.preview?.description).toBe("Plain words from the service.");
  expect(post).toHaveBeenCalledWith("/api/brands/{brandId}/triggers/rules/preview", { params: { path: { brandId: "brand-1" } }, body: readRule(form(source)).request });
  expect(result.preview?.request.condition?.[source === "weather" ? "minUnits" : "threshold"]).toBeNull();
  expect(refresh).not.toHaveBeenCalled();
});
test.each([null, "rule-1"])("saves only the exact previewed request for %s", async (id) => {
  const data = form(); const preview = await previewOrSaveRule("brand-1", id, idle, data);
  data.set("intent", "save"); post.mockResolvedValue({ data: {}, response: { status: 201, ok: true } });
  expect((await previewOrSaveRule("brand-1", id, preview, data)).status).toBe("saved");
  expect(id ? put : post).toHaveBeenLastCalledWith(id ? "/api/brands/{brandId}/triggers/rules/{ruleId}" : "/api/brands/{brandId}/triggers/rules", { params: { path: id ? { brandId: "brand-1", ruleId: id } : { brandId: "brand-1" } }, body: readRule(data).request });
  expect(refresh).toHaveBeenCalledOnce();
});
test("requires a fresh preview after edits and rejects malformed numbers", async () => {
  const data = form(); const preview = await previewOrSaveRule("brand-1", null, idle, data);
  post.mockClear(); data.set("intent", "save"); data.set("name", "Changed rule");
  expect(await previewOrSaveRule("brand-1", null, preview, data)).toMatchObject({ status: "error", message: expect.stringContaining("Preview") });
  data.set("condition.threshold", "NaN");
  expect(await previewOrSaveRule("brand-1", null, idle, data)).toMatchObject({ fieldErrors: { "condition.threshold": ["Enter a valid number."] } });
  expect(post).not.toHaveBeenCalled();
});
test.each(["pause", "resume", "activate"] as const)("scopes %s to the requested brand and rule", async (operation) => {
  expect((await changeRule("brand-1", "rule-1", operation, "acme", idle, new FormData())).status).toBe("saved");
  expect(post).toHaveBeenCalledWith(`/api/brands/{brandId}/triggers/rules/{ruleId}/${operation}`, { params: { path: { brandId: "brand-1", ruleId: "rule-1" } } });
});
test("deleting returns to the rules list rather than refreshing a missing detail", async () => {
  await expect(changeRule("brand-1", "rule-1", "delete", "acme", idle, new FormData())).rejects.toThrow("redirect");
  expect(remove).toHaveBeenCalledWith("/api/brands/{brandId}/triggers/rules/{ruleId}", { params: { path: { brandId: "brand-1", ruleId: "rule-1" } } });
  expect(revalidatePath).toHaveBeenCalledWith("/acme/triggers"); expect(redirect).toHaveBeenCalledWith("/acme/triggers");
});

test.each([400, 403, 404, 409])("preserves service errors (%s) without refreshing", async (status) => {
  const error = status === 400 ? { errors: { "condition.eventId": ["Choose a calendar event."] } } : { title: "The rule cannot be changed." };
  post.mockResolvedValue({ error, response: { status, ok: false } });
  const result = await previewOrSaveRule("brand-1", null, idle, form());
  expect(result.status).toBe("error");
  expect(status === 400 ? result.fieldErrors?.["condition.eventId"] : result.message).toEqual(status === 400 ? ["Choose a calendar event."] : "The rule cannot be changed.");
  expect(refresh).not.toHaveBeenCalled();
});
test("redirects expired sessions and returns network failures inline", async () => {
  post.mockResolvedValue({ response: { status: 401, ok: false } });
  await expect(previewOrSaveRule("brand-1", null, idle, form())).rejects.toThrow("sign-in");
  await expect(changeRule("brand-1", "rule-1", "pause", "acme", idle, new FormData())).rejects.toThrow("sign-in");
  remove.mockRejectedValue(new Error("offline"));
  expect(await changeRule("brand-1", "rule-1", "delete", "acme", idle, new FormData())).toMatchObject({ status: "error", message: expect.stringContaining("couldn't reach") });
});
