import { expect, test } from "vitest";

import { opportunityFilters } from "./filters";

test("accepts only supported filters and trims a category", () => {
  expect(opportunityFilters({ status: "saved", category: " Trainers ", watched: "true", minScore: "70" })).toEqual({ status: "saved", category: "Trainers", watched: true, minScore: 70 });
});

test.each(["-1", "101", "NaN", "1.5", "1e2", ""] )("does not send an invalid minimum score %s to the service", (minScore) => {
  expect(opportunityFilters({ minScore }).minScore).toBeUndefined();
});

test("ignores repeated query values and unsupported status values", () => {
  expect(opportunityFilters({ status: ["new", "saved"], watched: "false", category: ["Shoes"], minScore: ["80"] })).toEqual({ status: "open", category: "", watched: undefined, minScore: undefined });
  expect(opportunityFilters({ status: "anything" }).status).toBe("open");
  expect(opportunityFilters({ minScore: "0" }).minScore).toBe(0);
});
