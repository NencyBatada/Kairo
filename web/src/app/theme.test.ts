import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, test } from "vitest";

const css = readFileSync(join(process.cwd(), "src", "app", "globals.css"), "utf8");

function tokens(block: string): Record<string, string> {
  return Object.fromEntries(
    [...block.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-f]{6})\s*;/gi)].map(([, name, hex]) => [name, hex.toLowerCase()]),
  );
}

const light = tokens(css.match(/^:root\s*\{([^}]*)\}/m)?.[1] ?? "");
const dark = tokens(css.match(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{([^}]*)\}/)?.[1] ?? "");

function luminance(hex: string) {
  const [r, g, b] = [1, 3, 5]
    .map((start) => parseInt(hex.slice(start, start + 2), 16) / 255)
    .map((channel) => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(foreground: string, background: string) {
  const [high, low] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (high + 0.05) / (low + 0.05);
}

const textPairs: [string, string[]][] = [
  ["foreground", ["background", "sidebar", "card", "muted", "accent", "primary-soft"]],
  ["muted-foreground", ["background", "sidebar", "card", "muted", "accent"]],
  ["subtle-foreground", ["background", "sidebar", "card", "muted", "accent"]],
  ["primary", ["background", "sidebar", "card", "muted"]],
  ["primary-foreground", ["primary", "primary-hover"]],
  ["primary-strong", ["primary-soft"]],
  ["success-strong", ["success-soft", "card", "background"]],
  ["warning-strong", ["warning-soft", "card", "background"]],
  ["destructive", ["card", "background"]],
  ["destructive-strong", ["destructive-soft", "card", "background"]],
  ["destructive-foreground", ["destructive", "destructive-hover"]],
  ["info-strong", ["info-soft", "card", "background"]],
];

const markPairs: [string, string[]][] = [
  ["input", ["card", "background", "sidebar", "muted", "accent", "primary-soft"]],
  ["ring", ["background", "sidebar", "card"]],
  ["primary", ["primary-soft", "accent"]],
  ["success", ["card", "success-soft"]],
  ["warning", ["card", "warning-soft", "sidebar"]],
  ["destructive", ["destructive-soft"]],
  ["info", ["card", "info-soft"]],
];

const pairs = (list: [string, string[]][]) => list.flatMap(([foreground, backgrounds]) => backgrounds.map((background) => [foreground, background]));

test("both themes define the same colour tokens", () => {
  expect(Object.keys(light).length).toBeGreaterThan(40);
  expect(Object.keys(dark).sort()).toEqual(Object.keys(light).sort());
});

describe.each([
  ["light", light],
  ["dark", dark],
])("the %s theme", (_, theme) => {
  test.each(pairs(textPairs))("%s text on %s reaches 4.5:1", (foreground, background) => {
    expect(contrast(theme[foreground], theme[background])).toBeGreaterThanOrEqual(4.5);
  });

  test.each(pairs(markPairs))("%s on %s reaches 3:1", (foreground, background) => {
    expect(contrast(theme[foreground], theme[background])).toBeGreaterThanOrEqual(3);
  });
});
