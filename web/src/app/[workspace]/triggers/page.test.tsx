import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import TriggersPage from "./page";
import RulePage from "./[id]/page";
import NewRulePage from "./new/page";
import { options, festival, rule } from "./fixtures.test-data";

const { getWorkspace, coreApi, unwrap, get } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), get: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("./actions", () => ({ changeRule: vi.fn(), previewOrSaveRule: vi.fn() }));
beforeEach(() => {
  vi.clearAllMocks(); getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["admin"] });
  coreApi.mockResolvedValue({ GET: get }); unwrap.mockImplementation(async (result) => result.data);
  get.mockImplementation(async (path: string) => ({ data: path.endsWith("/options") ? options : path.endsWith("/festivals") ? [festival] : path.endsWith("/rules/{ruleId}") ? { rule, firings: [] } : path.endsWith("/rules") ? [rule] : { timeZone: "Asia/Kolkata" } }));
});
const params = Promise.resolve({ workspace: "acme", id: "rule-1" });
test("loads the selected status and links rules and the festival calendar", async () => {
  render(await TriggersPage({ params, searchParams: Promise.resolve({ status: "draft" }) }));
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/triggers/rules", { params: { path: { brandId: "brand-1" }, query: { status: "draft" } } });
  expect(screen.getByRole("link", { name: /Rain in Mumbai/ }).getAttribute("href")).toBe("/acme/triggers/rule-1");
  expect(screen.getByRole("link", { name: "Festival calendar" }).getAttribute("href")).toBe("/acme/triggers/festivals");
  expect(screen.getByRole("link", { name: "Create rule" })).toBeTruthy();
});
test("non-admin members can review but cannot create or edit rules", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  const view = render(await TriggersPage({ params, searchParams: Promise.resolve({}) }));
  expect(screen.queryByRole("link", { name: "Create rule" })).toBeNull(); view.unmount();
  render(await RulePage({ params }));
  expect(screen.queryByRole("button", { name: /Pause|Delete|Preview/ })).toBeNull();
  expect(screen.queryByLabelText("Rule name")).toBeNull();
});
test("rejects direct create navigation for non-admin members without loading editor data", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  render(await NewRulePage({ params }));
  expect(screen.getByText(/Only workspace admins/)).toBeTruthy(); expect(get).not.toHaveBeenCalled();
});
test("detail shows results, check status and editing controls without inventing lift", async () => {
  render(await RulePage({ params }));
  expect(screen.getByText(/Condition not met/)).toBeTruthy();
  expect(screen.getByRole("button", { name: "Pause rule" })).toBeTruthy();
  expect(screen.getByText(/Campaign lift is not available yet/)).toBeTruthy();
  expect(screen.getByText("This rule has not fired yet.")).toBeTruthy();
});
