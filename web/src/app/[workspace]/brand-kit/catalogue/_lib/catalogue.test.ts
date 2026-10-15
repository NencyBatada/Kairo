import { expect, test } from "vitest";

import { catalogueFilters, catalogueQuery, importOutcome } from "./catalogue";

test("validates paging and allowed filter values without dropping search and category", () => {
  expect(catalogueFilters({ page: "-2", availability: "anything", status: "removed", search: " boots ", category: "Men > Shoes", missing: "stock" })).toEqual({ page: 1, pageSize: 25, status: "removed", search: "boots", category: "Men > Shoes", missing: "stock" });
  expect(catalogueFilters({ page: "1.5", missing: "unknown" }).page).toBe(1);
  expect(catalogueFilters({ page: "99999999999999999999" }).page).toBe(1);
});

test("pagination retains every active filter and encodes category and search", () => {
  const filters = catalogueFilters({ search: "trail & road", category: "Men > Shoes", availability: "in_stock", missing: "any" });
  const query = new URLSearchParams(catalogueQuery(filters, 3));
  expect(query.get("search")).toBe("trail & road");
  expect(query.get("category")).toBe("Men > Shoes");
  expect(query.get("page")).toBe("3");
  expect(query.get("missing")).toBe("any");
});

test("a 201 import in failed or suspicious state is never presented as success", () => {
  expect(importOutcome({ status: "failed", errorMessage: "No id column." }).status).toBe("error");
  expect(importOutcome({ status: "suspicious" }).message).toContain("unchanged");
  expect(importOutcome({ status: "unchanged" }).status).toBe("saved");
  expect(importOutcome({ status: "succeeded" }).status).toBe("saved");
});
