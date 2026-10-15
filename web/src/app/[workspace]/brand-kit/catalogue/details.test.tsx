import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { imported, product } from "./_lib/fixtures.test-data";
import ImportPage from "./imports/[importId]/page";
import ProductPage from "./products/[productId]/page";

const { catalogueRequest, getWorkspace, coreApi, unwrap, notFound } = vi.hoisted(() => ({ catalogueRequest: vi.fn(), getWorkspace: vi.fn(), coreApi: vi.fn(), unwrap: vi.fn(), notFound: vi.fn() }));
vi.mock("./_lib/api", () => ({ catalogueRequest, catalogueJson: async (response: Response) => response.json() }));
vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));
vi.mock("./actions", () => ({ fixProduct: vi.fn() }));
vi.mock("next/navigation", () => ({ notFound }));
beforeEach(() => {
  vi.clearAllMocks(); getWorkspace.mockResolvedValue({ id: "b", roles: ["marketer"] });
  coreApi.mockResolvedValue({ GET: async () => ({ data: { timeZone: "Asia/Kolkata" } }) });
  unwrap.mockImplementation(async (result) => result.data);
  notFound.mockImplementation(() => { throw new Error("not found"); });
});
test("marketers can fix missing product details and unsafe links are not clickable", async () => {
  catalogueRequest.mockResolvedValue(new Response(JSON.stringify({ ...product, link: "javascript:alert(1)" })));
  render(await ProductPage({ params: Promise.resolve({ workspace: "acme", productId: "product-1" }) }));
  expect(screen.getByRole("button", { name: "Save missing details" })).toBeTruthy();
  expect(screen.queryByRole("link", { name: "View product" })).toBeNull();
  expect(catalogueRequest).toHaveBeenCalledWith("b", "/products/product-1");
});
test("missing products return not found", async () => {
  catalogueRequest.mockResolvedValue(new Response(null, { status: 404 }));
  await expect(ProductPage({ params: Promise.resolve({ workspace: "acme", productId: "missing" }) })).rejects.toThrow("not found");
});
test("import detail exposes the row and plain reason for invalid data", async () => {
  catalogueRequest.mockResolvedValue(new Response(JSON.stringify({ ...imported, invalid: 1, problems: [{ row: 3, attribute: "price", message: "Use a valid price." }] })));
  render(await ImportPage({ params: Promise.resolve({ workspace: "acme", importId: "import-1" }) }));
  expect(screen.getByText("Use a valid price.")).toBeTruthy();
  expect(screen.getByText(/Row 3/)).toBeTruthy();
});
