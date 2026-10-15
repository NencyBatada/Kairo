import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import TopicsPage from "./page";

const { getWorkspace, coreApi, unwrap, get } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), get: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("./actions", () => ({ addTopic: vi.fn(), removeTopic: vi.fn() }));
beforeEach(() => {
  vi.clearAllMocks();
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  get.mockResolvedValue({ data: [{ id: "topic-1", phrase: "Rain shoes", kind: "watch", createdAt: "2026-10-15T01:00:00Z" }, { id: "topic-2", phrase: "Winter boots", kind: "ignore", createdAt: "2026-10-15T01:00:00Z" }] });
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => result.data);
});
const page = () => TopicsPage({ params: Promise.resolve({ workspace: "acme" }) });

test("loads brand-scoped watched and ignored lists and explains the stock restriction", async () => {
  render(await page());
  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/opportunities/topics", { params: { path: { brandId: "brand-1" } } });
  expect(screen.getByRole("heading", { level: 1, name: "Watched and ignored topics" })).toBeTruthy();
  expect(screen.getByRole("list", { name: "Watched topics" }).textContent).toContain("Rain shoes");
  expect(screen.getByRole("list", { name: "Ignored topics" }).textContent).toContain("Winter boots");
  expect(screen.getByText(/when matching products are in stock/)).toBeTruthy();
  expect(screen.getByRole("link", { name: "All opportunities" }).getAttribute("href")).toBe("/acme/opportunities");
});

test.each(["admin", "marketer"])("allows %s users to manage topics", async (role) => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: [role] });
  render(await page());
  expect(screen.getByRole("button", { name: "Add topic" })).toBeTruthy();
  expect(screen.getAllByRole("button", { name: /Remove / })).toHaveLength(2);
});

test("approvers see a read-only list and no mutation controls", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["approver"] });
  render(await page());
  expect(screen.queryByRole("button")).toBeNull();
  expect(screen.getByText("Only workspace admins and marketers can manage topics.")).toBeTruthy();
  expect(screen.getByText("Winter boots")).toBeTruthy();
});

test("provides helpful empty states for both preferences", async () => {
  get.mockResolvedValue({ data: [] });
  render(await page());
  expect(screen.getByText(/No watched topics yet/)).toBeTruthy();
  expect(screen.getByText(/No ignored topics yet/)).toBeTruthy();
});
