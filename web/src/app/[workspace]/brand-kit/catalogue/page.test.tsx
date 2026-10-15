import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { product, summary } from "./_lib/fixtures.test-data";
import CataloguePage from "./page";

const { getWorkspace, coreApi, unwrap, catalogueRequest } = vi.hoisted(() => ({ getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), catalogueRequest: vi.fn() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("./_lib/api", () => ({ catalogueRequest, catalogueJson: async (response: Response) => { if (!response.ok) throw new Error("Catalogue unavailable"); return response.json(); } }));
vi.mock("./actions", () => ({ setFeed: vi.fn(), refreshFeed: vi.fn(), stopFeed: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh: vi.fn() }), usePathname: () => "/acme/brand-kit/catalogue" }));
beforeEach(() => {
  vi.clearAllMocks(); getWorkspace.mockResolvedValue({ id: "b", roles: ["admin"] });
  coreApi.mockResolvedValue({ GET: async () => ({ data: { currency: "INR", timeZone: "Asia/Kolkata" } }) });
  unwrap.mockImplementation(async (result) => result.data);
  catalogueRequest.mockImplementation(async (_brand: string, path: string) => new Response(JSON.stringify(path === "/summary" ? summary : path === "/imports" ? [] : { items: [product], page: 1, pageSize: 25, total: 1 })));
});
const props = { params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve({ search: "trail" }) };
test("loads the scoped catalogue with active filters and role-specific controls", async () => {
  render(await CataloguePage(props));
  expect(catalogueRequest).toHaveBeenCalledWith("b", expect.stringContaining("search=trail"));
  expect(screen.getByRole("heading", { name: "Catalogue", level: 1 })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Trail runner" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Import file" })).toBeTruthy();
});
test("missing backend endpoints are an explicit connection state, not an empty catalogue", async () => {
  catalogueRequest.mockResolvedValue(new Response(null, { status: 404 }));
  render(await CataloguePage(props));
  expect(screen.getByText(/Catalogue import is not available yet/)).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Import file" })).toBeNull();
});
