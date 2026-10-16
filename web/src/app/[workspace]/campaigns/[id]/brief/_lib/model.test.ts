import { expect, test } from "vitest";
import { editForm } from "../fixtures.test-data";
import { day, money, percent, readEdit, version } from "./model";

test("parses all fields, preserving exact decimal input and trimmed lines", () => {
  const { body, errors } = readEdit(editForm({ budget: "25000.45", audiences: "  Commuters \n\n Runners " }));
  expect(errors).toEqual({}); expect(body.budget).toBe("25000.45"); expect(body.audiences).toEqual(["Commuters", "Runners"]);
  expect(body).toMatchObject({ version: 1, briefVersion: 2, productIds: ["p1"], campaignTypes: ["search"], languages: ["en"] });
});
test.each(["-1", "1.1", "2147483648", "1e3", "", "foo"])("rejects invalid versions %s", input => expect(version(input)).toBeNull());
test.each([
  ["budget", "1e5"], ["budget", "0"], ["budget", "3.456"], ["budget", "1000000000000"],
  ["endDate", "2026-02-30"], ["startDate", "2026-13-01"], ["startDate", "0000-01-01"], ["endDate", "2026-10-17"],
  ["title", ""], ["objective", "x".repeat(301)], ["audiences", "x".repeat(201)], ["languages", []],
  ["landingPages", "javascript:alert(1)"], ["landingPages", "https://user:pass@example.com"], ["regions", []],
] as [string, string | string[]][])("rejects invalid %s input", (name, input) => expect(readEdit(editForm({ [name]: input })).errors[name]).toBeTruthy());
test("formats money, fractions and date-only values without time-zone drift", () => {
  expect(money("200000", "INR")).toBe("₹2,00,000.00"); expect(percent("0.12")).toBe("12.0%"); expect(percent(null)).toBe("Not available"); expect(day("2026-10-18")).toBe("18 Oct 2026");
});
