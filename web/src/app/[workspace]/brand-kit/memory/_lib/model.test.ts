import { expect, test } from "vitest";
import { filters, kindName, sourceAnalysis, stamp, validateEdit, validId, validVersion } from "./model";
import { brandId, entry } from "../fixtures.test-data";

test("filters whitelist supported values and default to active", () => { expect(filters({ kind: "message", outcome: "didnt_work", state: "removed" })).toEqual({ kind: "message", outcome: "didnt_work", state: "removed" }); expect(filters({ kind: ["message"], outcome: "unknown", state: "all" })).toEqual({ kind: undefined, outcome: undefined, state: "active" }); });
test.each([["", "lesson"], ["title", " "], ["t".repeat(121), "lesson"], ["title", "l".repeat(501)]])("validates non-empty bounded titles and lessons", (title, lesson) => expect(validateEdit(title, lesson)?.status).toBe("error"));
test("validation trims without rejecting the allowed maximum", () => { expect(validateEdit(" t ", " l ")).toBeNull(); expect(validateEdit("t".repeat(120), "l".repeat(500))).toBeNull(); });
test("requires valid IDs and integer versions", () => { expect(validId(brandId)).toBe(true); expect(validId("bad")).toBe(false); expect(validVersion(3)).toBe(true); for (const value of ["3", 0, -1, 1.5, Infinity, 2147483648]) expect(validVersion(value)).toBe(false); });
test("timestamps use workspace timezone and do not invent missing dates", () => { expect(stamp("2026-10-15T00:00:00Z", "Asia/Kolkata")).toContain("5:30"); expect(stamp(null, "Asia/Kolkata")).toBe("Date unavailable"); });

test("Impact Lab change entries can be filtered and have validated source identities", () => { expect(filters({ kind: "change" }).kind).toBe("change"); expect(kindName("change")).toBe("Change"); expect(sourceAnalysis(entry({ kind: "change", evidence: { labAnalysisId: brandId } }))).toBe(brandId); expect(sourceAnalysis(entry({ kind: "message", evidence: { labAnalysisId: brandId } }))).toBeNull(); expect(sourceAnalysis(entry({ kind: "change", evidence: { labAnalysisId: "//unsafe" } }))).toBeNull(); });
