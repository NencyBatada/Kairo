import { expect, test } from "vitest";

import { liveStreamUrl, WORKSPACE_LIVE_EVENTS } from "./live-events";

test("data feed runs refresh the workspace like connection changes do", () => {
  expect(WORKSPACE_LIVE_EVENTS).toContain("connection.changed");
  expect(WORKSPACE_LIVE_EVENTS).toContain("feeds.changed");
});

test("the live stream lives under the brand", () => {
  expect(liveStreamUrl("b1")).toBe("/api/brands/b1/live");
});
