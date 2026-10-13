import { expect, test } from "vitest";

import { cn } from "@/lib/utils";

test("keeps a custom text size next to a text colour", () => {
  expect(cn("text-display-lg", "text-foreground")).toBe("text-display-lg text-foreground");
  expect(cn("text-small", "text-subtle-foreground")).toBe("text-small text-subtle-foreground");
});

test("lets a later text size replace an earlier one, custom or not", () => {
  expect(cn("text-sm", "text-small")).toBe("text-small");
  expect(cn("text-eyebrow", "text-xs")).toBe("text-xs");
});

test("merges conflicting utilities and drops falsy values", () => {
  expect(cn("px-2 py-1", false, "px-4", undefined)).toBe("py-1 px-4");
});
