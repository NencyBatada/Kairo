import { expect, test } from "vitest";

import { refreshMessage } from "./refresh-message";

test("turns connection and stock refresh codes into useful next steps", () => {
  expect(refreshMessage("needs_reconnect")).toContain("Reconnect Google Ads");
  expect(refreshMessage("no_stock")).toContain("Update the catalogue");
  expect(refreshMessage("no_seeds")).toContain("Approve a Brand Kit");
  expect(refreshMessage("busy")).toContain("already running");
});

test("gives a safe fallback for unknown refresh reasons", () => {
  expect(refreshMessage("internal_code")).toBe(refreshMessage(null));
});
