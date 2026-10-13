import { expect, test } from "vitest";

import { timeZoneOptions } from "./time-zones";

test("offers India's time zone under its current name", () => {
  const zones = timeZoneOptions();

  expect(zones).toContain("Asia/Kolkata");
  expect(zones).not.toContain("Asia/Calcutta");
});

test("renames old zone names and keeps the list sorted", () => {
  expect(timeZoneOptions(["Europe/Kiev", "Africa/Abidjan"])).toEqual(["Africa/Abidjan", "Asia/Kolkata", "Europe/Kyiv"]);
});
