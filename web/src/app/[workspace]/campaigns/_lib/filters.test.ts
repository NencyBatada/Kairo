import { expect, test } from "vitest";

import { ownerId } from "../fixtures.test-data";
import { apiFilters, campaignFilters, filterQuery, validDay } from "./filters";

test("maps URL filters to the exact generated API query contract", () => {
  const { values, errors } = campaignFilters({ q: " Rain Runner ", stage: "brief_draft", status: "waiting_approval", owner: ownerId, from: "2026-10-01", to: "2026-10-31", before: "639276282000000000" });
  expect(errors).toEqual({});
  expect(apiFilters(values)).toEqual({ Q: "Rain Runner", Stage: "brief_draft", Status: "waiting_approval", Owner: ownerId, From: "2026-10-01", To: "2026-10-31", Before: "639276282000000000" });
});

test("empty controls become optional query parameters", () => {
  expect(Object.values(apiFilters(campaignFilters({ q: " " }).values)).every((value) => value === undefined)).toBe(true);
});

test.each(["2026-02-29", "2026-02-30", "0000-01-01", "2026-1-01", "bad"])("rejects invalid dates: %s", (value) => expect(validDay(value)).toBe(false));
test.each(["2028-02-29", "2026-10-15", "0001-01-01", "9999-12-31"])("accepts valid dates: %s", (value) => expect(validDay(value)).toBe(true));

test("validates search length, enums, owner ID and date order", () => {
  expect(campaignFilters({ q: "x".repeat(101), stage: "toString", status: "__proto__", owner: "all", from: "2026-10-20", to: "2026-10-10" }).errors).toEqual({ q: expect.any(String), stage: expect.any(String), status: expect.any(String), owner: expect.any(String), to: expect.any(String) });
});

test.each(["0", "bad", "3155378976000000000", "999999999999999999999999", ["1", "2"]])("validates opaque paging cursors: %s", (before) => {
  expect(campaignFilters({ before }).errors.before).toBeTruthy();
});

test("paging retains all filters, preserves special search text and resets the old cursor", () => {
  const values = campaignFilters({ q: "rain & sun", status: "in_progress", before: "12" }).values;
  const older = new URLSearchParams(filterQuery(values, "8"));
  expect(older.get("q")).toBe("rain & sun"); expect(older.get("status")).toBe("in_progress"); expect(older.get("before")).toBe("8");
  expect(new URLSearchParams(filterQuery(values)).has("before")).toBe(false);
});
