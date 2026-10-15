import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { catalogueFilters } from "../_lib/catalogue";
import { product } from "../_lib/fixtures.test-data";
import { ProductList } from "./product-list";

test("shows missing values honestly and retains filters in paging links", () => {
  render(<ProductList products={{ items: [product], page: 2, pageSize: 25, total: 75 }} filters={catalogueFilters({ page: "2", search: "trail & road", missing: "price" })} categories={[]} handle="acme" currency="INR" />);
  expect(screen.getByRole("link", { name: "Trail runner" }).getAttribute("href")).toBe("/acme/brand-kit/catalogue/products/product-1");
  expect(screen.getByText("Price missing")).toBeTruthy();
  expect(screen.getByText("Stock missing")).toBeTruthy();
  const href = screen.getByRole("link", { name: "Next page" }).getAttribute("href")!;
  expect(new URL(href, "https://example.com").searchParams.get("search")).toBe("trail & road");
  expect(new URL(href, "https://example.com").searchParams.get("page")).toBe("3");
});
test("shows a useful filtered empty state and no paging links when no products match", () => {
  render(<ProductList products={{ items: [], page: 1, pageSize: 25, total: 0 }} filters={catalogueFilters({ missing: "stock" })} categories={[]} handle="acme" currency="INR" />);
  expect(screen.getByText("No products match these filters.")).toBeTruthy();
  expect(screen.queryByRole("link", { name: "Next page" })).toBeNull();
});

test("soft navigation to cleared filters resets the search and picker values", () => {
  const props = { products: { items: [], page: 1, pageSize: 25, total: 0 }, categories: [], handle: "acme", currency: "INR" };
  const { rerender } = render(<ProductList {...props} filters={catalogueFilters({ search: "trail", missing: "price" })} />);
  rerender(<ProductList {...props} filters={catalogueFilters({})} />);
  expect((screen.getByLabelText("Search title or product ID") as HTMLInputElement).value).toBe("");
  expect((screen.getByLabelText("Missing details") as HTMLSelectElement).value).toBe("");
});
