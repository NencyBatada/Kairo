import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";

import { expect, test } from "vitest";

import { HANDLED_LIVE_EVENTS, liveStreamUrl, WORKSPACE_LIVE_EVENTS } from "./live-events";

const coreSource = path.resolve(__dirname, "../../../core/src");

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      return ["bin", "obj", "Migrations"].includes(entry.name) ? [] : sourceFiles(full);
    }
    return entry.name.endsWith(".cs") ? [full] : [];
  });
}

function eventsCorePublishes() {
  const types = new Set<string>();
  const literal = /new(?:\s+LiveEvent)?\s*\(\s*"([a-z_]+(?:\.[a-z_]+)+)"/g;
  for (const file of sourceFiles(coreSource)) {
    const text = readFileSync(file, "utf8");
    if (!/\bLiveEvent\b/.test(text)) {
      continue;
    }
    for (const match of text.matchAll(literal)) {
      types.add(match[1]);
    }
  }
  return [...types].sort();
}

test.skipIf(!existsSync(coreSource))("handles every live event the core publishes", () => {
  const published = eventsCorePublishes();

  expect(published.length).toBeGreaterThan(0);
  expect(published).toContain("agent.progress");
  expect([...HANDLED_LIVE_EVENTS].sort()).toEqual([...new Set([...published, "catalogue.imported", "catalogue.product_changed"])].sort());
});

test("builds the workspace stream URL", () => {
  expect(liveStreamUrl("brand-1")).toBe("/api/brands/brand-1/live");
});

test("data feed runs refresh the workspace like connection changes do", () => {
  expect(WORKSPACE_LIVE_EVENTS).toContain("connection.changed");
  expect(WORKSPACE_LIVE_EVENTS).toContain("feeds.changed");
});

test("catalogue imports and product fixes refresh product screens", () => {
  expect(WORKSPACE_LIVE_EVENTS).toContain("catalogue.imported");
  expect(WORKSPACE_LIVE_EVENTS).toContain("catalogue.product_changed");
});

test("opportunity refreshes, decisions and topic changes refresh the opportunity screens", () => {
  expect(WORKSPACE_LIVE_EVENTS).toContain("opportunities.refreshed");
  expect(WORKSPACE_LIVE_EVENTS).toContain("opportunity.changed");
  expect(WORKSPACE_LIVE_EVENTS).toContain("opportunity_topics.changed");
});
