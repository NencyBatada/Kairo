import { expect, test } from "vitest";

import { initials } from "./initials";

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
