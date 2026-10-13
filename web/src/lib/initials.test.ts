import { expect, test } from "vitest";

import { initial, initials } from "./initials";

test.each([
  ["Priya Shah", "PS"],
  ["priya", "P"],
  ["Neha  Rao Kapoor", "NK"],
  ["  ", "?"],
  ["नेहा कपूर", "नेक"],
  ["kabir@agency.example", "K"],
])("the initials of %j are %s", (name, expected) => {
  expect(initials(name)).toBe(expected);
});

test.each([
  ["Acme Footwear", "A"],
  ["  monsoon", "M"],
  ["🚀 Rocket Co", "🚀"],
  ["नेहा कपूर", "ने"],
  ["  ", "?"],
])("the single initial of %j is %s, never half a character", (name, expected) => {
  expect(initial(name)).toBe(expected);
});
