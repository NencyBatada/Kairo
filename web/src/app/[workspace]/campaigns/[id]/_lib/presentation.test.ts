import { expect, test } from "vitest";

import { dates, elapsed, stageSegment } from "./presentation";

test.each([[0, "Less than a minute"], [1, "1 minute"], [59, "59 minutes"], [60, "1 hour, 0 min"], [125, "2 hours, 5 min"], [1440, "1 day, 0 hr"], [2940, "2 days, 1 hr"]])("formats %i minutes in the current stage", (minutes, expected) => {
  expect(elapsed("2026-10-15T00:00:00Z", Date.parse("2026-10-15T00:00:00Z") + Number(minutes) * 60_000)).toBe(expected);
});

test("handles clock skew and invalid timestamps", () => {
  expect(elapsed("2026-10-16T00:00:00Z", Date.parse("2026-10-15T00:00:00Z"))).toBe("Less than a minute");
  expect(elapsed("invalid", Date.now())).toBe("Time unavailable");
});

test("routes stages to their review screen and formats workspace times", () => {
  expect(stageSegment("asset_review")).toBe("review");
  expect(stageSegment("discarded")).toBeUndefined();
  expect(dates("Asia/Kolkata").format(new Date("2026-10-15T03:30:00Z"))).toMatch(/9:00/i);
});
