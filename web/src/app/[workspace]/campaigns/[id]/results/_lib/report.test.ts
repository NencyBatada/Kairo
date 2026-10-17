import { expect, test } from "vitest";
import { date, liftRange, money, numeric, percent, range, reportFigures, timestamp } from "./report";
import { reading, report } from "../fixtures.test-data";

test.each([null, undefined, "", " ", "bad", Infinity, NaN, {}, true])("missing or invalid numbers are not zero (%s)", value => expect(numeric(value)).toBeNull());
test("numeric strings, zero, signed percentages and Indian currency retain their meaning", () => { expect(numeric("0")).toBe(0); expect(percent(-.12)).toBe("−12.0%"); expect(money(200000, "INR")).toBe("₹2,00,000.00"); expect(money(4, "invalid")).toBe("4 invalid"); });
test.each([[1, null, 2], [1, 2, 3], [1, -1, Infinity]])("incomplete and unordered ranges are unavailable", (estimate, low, high) => expect(range(estimate, low, high)).toBeNull());
test("complete and zero-width ranges are accepted", () => { expect(range("0", "0", "0")).toEqual({ value: 0, low: 0, high: 0 }); expect(liftRange(reading())).toEqual({ value: .12, low: .06, high: .2 }); });
test.each([{ early: true }, { status: "insufficient_data" }, { high: null }])("interim or insufficient readings never become final lift", overrides => expect(liftRange(reading(overrides))).toBeNull());
test("report metrics preserve server costs and distinguish conversions from extra outcomes", () => { const figures = reportFigures(report(), reading()); expect(figures[0]).toMatchObject({ value: "12", note: "90% range: 6 to 20" }); expect(figures[2].value).toBe("30"); expect(figures[3]).toMatchObject({ value: "₹100.00", note: "90% range: ₹60.00 to ₹200.00" }); });
test("missing positive outcomes and one-sided cost ranges have explicit explanations", () => { expect(reportFigures(report({ costPerExtra: null }), reading())[3].value).toBe("Not available"); expect(reportFigures(report({ costPerExtraHigh: null }), reading())[3].note).toContain("no finite upper bound"); expect(reportFigures(report(), reading({ extraHigh: null }))[0].value).toBe("Not available"); });
test("date-only values stay in their calendar day and instants use the workspace zone", () => { expect(date("2026-10-01")).toBe("1 Oct 2026"); expect(date("bad")).toBe("Date unavailable"); expect(timestamp(null, "Asia/Kolkata")).toBe("Date unavailable"); expect(timestamp("2026-10-15T00:00:00Z", "Asia/Kolkata")).toContain("5:30"); });
