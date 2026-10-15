import { fireEvent, render, screen } from "@testing-library/react";
import { renderToString } from "react-dom/server";
import { expect, test } from "vitest";

import { opportunity } from "./fixtures.test-data";
import { growth, webUrl } from "./format";
import { OpportunityProducts } from "./products";
import { OpportunityTrend } from "./trend";

test("formats fractional growth and distinguishes a missing comparison from zero", () => {
  expect(growth("0.124")).toBe("+12.4%");
  expect(growth(-0.031)).toBe("−3.1%");
  expect(growth(null)).toBe("Not available");
  expect(growth(0)).toBe("+0.0%");
});

test("only uses HTTP links for external sources and product media", () => {
  expect(webUrl("javascript:alert(1)")).toBeUndefined();
  expect(webUrl("//shop.example/image")).toBeUndefined();
  expect(webUrl("https://shop.example/item")).toBe("https://shop.example/item");
});

test("shows all twelve months in the chart's data table and exposes hover values", () => {
  render(<OpportunityTrend points={opportunity.trend} />);
  expect(screen.getByRole("img").getAttribute("aria-label")).toContain("Jan 2025 to Dec 2025");
  expect(screen.getAllByRole("row", { hidden: true })).toHaveLength(13);
  fireEvent.mouseEnter(document.querySelector("circle")!);
  expect(screen.getByText("Jan 2025 · 1,000 searches")).toBeTruthy();
});

test("handles empty and flat trends without invalid SVG coordinates", () => {
  const { rerender } = render(<OpportunityTrend points={[]} />);
  expect(screen.getByText("Search trend is not available yet.")).toBeTruthy();
  rerender(<OpportunityTrend points={[{ month: "2026-01", searches: 0 }]} />);
  expect(document.querySelector("path")?.getAttribute("d")).not.toMatch(/NaN|Infinity/);
});

test("server-renders chart tooltip titles as single text nodes for hydration", () => {
  const html = renderToString(<OpportunityTrend points={opportunity.trend} />);
  expect(html).toContain("<title>Jan 2025: 1,000 searches</title>");
});

test("shows product price, stock and safe links, with missing data fallbacks", () => {
  const { rerender } = render(<OpportunityProducts products={opportunity.products} />);
  expect(screen.getByText(/₹2,499.00/)).toBeTruthy();
  expect(screen.getByRole("link").getAttribute("rel")).toContain("noopener");
  rerender(<OpportunityProducts products={[{ ...opportunity.products[0], price: null, imageLink: "javascript:alert(1)", link: null }]} />);
  expect(screen.queryByRole("link")).toBeNull();
  expect(screen.getByText("No image")).toBeTruthy();
  expect(screen.getByText(/Price not available/)).toBeTruthy();
});
