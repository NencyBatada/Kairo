# Kairo Design System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Kairo's Warm Editorial design system (tokens, fonts, shadcn/ui components on Base UI) and rebuild every existing web screen on it, responsive from 320px phones to wide desktops.

**Architecture:** Colour, type, radius and shadow tokens live as CSS variables in `web/src/app/globals.css` and reach Tailwind through `@theme`. Primitives in `web/src/components/ui/` are shadcn/ui's Base UI components restyled to the tokens. Forms keep native HTML controls, and Base UI is used only for menus, sheets and dialogs. Kairo's composites in `web/src/components/` (shell, navigation, page header, empty state) are built from the primitives, and every screen under `web/src/app/` is rebuilt from the composites.

**Tech Stack:** Next.js 16.4 (App Router, Cache Components), React 19.3, TypeScript, Tailwind CSS 4.3 through `@tailwindcss/turbopack`, Base UI 1.9, class-variance-authority, `cn`, Lucide, tw-animate-css, Vitest 5 with Testing Library and jsdom.

**Spec:** `docs/superpowers/specs/2026-10-13-design-system-design.md`, with the design system itself in `docs/design.md`. Read both before starting.

## Global Constraints

- No comments in any code, including CSS and test files (`CLAUDE.md` §3).
- Commit messages are one plain sentence in the repository's style, such as "Add the display primitives", with no AI attribution or trailers.
- Run every command from the repository root `C:\Kairo`. Web commands use `npm --prefix web …`.
- Package versions: `@base-ui/react` 1.9.0, `class-variance-authority` 0.7.1, `cn` 0.4.0, `lucide-react` 1.56.0, `tw-animate-css` 1.4.0 (dev). Don't add `shadcn`, `clsx`, `tailwind-merge` or `@testing-library/user-event`.
- Colours come only from the tokens in `docs/design.md` §3. No raw hex values in components. The only exceptions are the Google G mark, which keeps Google's own colours, and `globals.css`.
- The serif (`font-display`) is used only at the display sizes (`text-display-sm` and up).
- Every interactive control keeps the accessible name the existing tests use, except the sign-in link, which becomes "Continue with Google".
- Base UI wrappers take `className?: string` only, because `cn` doesn't accept Base UI's state functions.
- Server components by default. Add `"use client"` only to files that use state, effects, browser APIs or Base UI.
- UI copy uses British English and sentence case, and buttons are verbs that name the result.
- Each task ends with `npm --prefix web test` passing in full, not just the new tests.

## Review Focus

1. **Long names and emails.** A 100-character brand name (the core's maximum) or a long email must truncate in the workspace switcher, the user menu and member rows without widening the layout, and the full text must stay available in a `title`. Tests: Task 7 (switcher), Task 11 (member row).
2. **Odd names.** A member with no Google photo, a one-word name, extra spaces or a Devanagari name still gets sensible initials, never an empty avatar. Tests: Task 4.
3. **Focus after overlays.** After closing the drawer, a menu or a confirmation, focus returns to the control that opened it, and confirmations start on Cancel. Tests: Tasks 5 and 8.
4. **320px phones.** Every screen fits a 320px-wide phone in both themes without sideways scrolling, except inside data tables. Test: the Task 15 harness measures `scrollWidth` on every screen.
5. **Members who aren't admins** see why controls are locked, rather than a silently disabled form. Tests: Task 10 (workspace and approvals forms).

---

## File map

| File | Responsibility | Task |
| --- | --- | --- |
| `web/src/lib/utils.ts` | `cn`, aware of the custom text sizes | 1 |
| `web/components.json` | shadcn configuration for future `shadcn add` | 1 |
| `web/src/app/globals.css` | Tokens, theme mapping, base styles, reduced motion | 2 |
| `web/src/app/layout.tsx` | Fonts, title template, theme colour | 2 |
| `web/src/app/theme.test.ts` | WCAG contrast of every token pair | 2 |
| `web/src/components/ui/button.tsx`, `input.tsx`, `native-select.tsx`, `checkbox.tsx`, `choice-chip.tsx`, `label.tsx`, `field.tsx` | Form primitives | 3 |
| `web/src/components/field-error.tsx`, `form-status.tsx`, `role-checkboxes.tsx` | Form feedback and role chips | 3 |
| `web/src/components/ui/card.tsx`, `badge.tsx`, `alert.tsx`, `avatar.tsx`, `skeleton.tsx`, `table.tsx` | Display primitives | 4 |
| `web/src/lib/initials.ts`, `web/src/components/user-avatar.tsx` | Avatar initials | 4 |
| `web/src/components/ui/dropdown-menu.tsx`, `sheet.tsx`, `alert-dialog.tsx`, `web/src/components/confirm-dialog.tsx` | Overlays | 5 |
| `web/src/components/logo.tsx`, `page-header.tsx`, `link-tabs.tsx`, `empty-state.tsx`, `site-header.tsx` | Page composites | 6 |
| `web/src/components/nav-menu.tsx`, `workspace-switcher.tsx`, `user-menu.tsx`, `sign-out-button.tsx`, settings and campaign-room tabs | Navigation | 7 |
| `web/src/components/app-shell.tsx`, `mobile-nav.tsx`, `skeletons.tsx`, `web/src/app/[workspace]/layout.tsx`, `_components/workspace-sidebar.tsx`, `_components/workspace-name.tsx` | The shell | 8 |
| `web/src/app/sign-in/**` | Sign-in | 9 |
| `web/src/app/welcome/**` | Welcome | 10 |
| `web/src/app/[workspace]/settings/**` | Settings | 11 |
| Area pages, campaign room, `page-placeholder.tsx` removed | Empty states | 12 |
| `web/src/app/not-found.tsx`, `error.tsx`, `[workspace]/not-found.tsx`, `[workspace]/error.tsx`, `not-found-state.tsx`, `error-state.tsx` | Not-found and error pages | 13 |
| `web/README.md`, `docs/Kairo-SAD.md`, `docs/design.md`, the spec | Docs | 14 |
| Scratchpad harness, not committed | Visual check | 15 |
| — | Final checks and integration | 16 |

---

### Task 1: Dependencies, `cn` and shadcn configuration

**Files:**
- Modify: `web/package.json`, `web/package-lock.json` (through npm)
- Create: `web/src/lib/utils.ts`
- Create: `web/src/lib/utils.test.ts`
- Create: `web/components.json`

**Interfaces:**
- Produces: `cn(...inputs: ClassValue[]): string` from `@/lib/utils`. It knows the custom sizes `text-display-xl`, `text-display-lg`, `text-display-md`, `text-display-sm`, `text-small` and `text-eyebrow`.

- [ ] **Step 1: Install the packages**

```bash
npm --prefix web install @base-ui/react@1.9.0 class-variance-authority@0.7.1 cn@0.4.0 lucide-react@1.56.0
npm --prefix web install --save-dev tw-animate-css@1.4.0
```

Expected: both finish without errors, and `web/package.json` lists the four dependencies and the dev dependency.

- [ ] **Step 2: Write the failing test**

Create `web/src/lib/utils.test.ts`:

```ts
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
```

- [ ] **Step 3: Run it and watch it fail**

Run: `npm --prefix web test -- src/lib/utils.test.ts`
Expected: FAIL, because `@/lib/utils` doesn't exist.

- [ ] **Step 4: Write `utils.ts`**

Create `web/src/lib/utils.ts`:

```ts
import { createCn } from "cn/config";

export const cn = createCn({
  extend: {
    classGroups: {
      "font-size": [{ text: ["display-xl", "display-lg", "display-md", "display-sm", "small", "eyebrow"] }],
    },
  },
});
```

- [ ] **Step 5: Run the test and watch it pass**

Run: `npm --prefix web test -- src/lib/utils.test.ts`
Expected: PASS, 3 tests.

- [ ] **Step 6: Add the shadcn configuration**

Create `web/components.json`:

```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "base-nova",
  "rsc": true,
  "tsx": true,
  "tailwind": {
    "config": "",
    "css": "src/app/globals.css",
    "baseColor": "neutral",
    "cssVariables": true,
    "prefix": ""
  },
  "iconLibrary": "lucide",
  "aliases": {
    "components": "@/components",
    "utils": "@/lib/utils",
    "ui": "@/components/ui",
    "lib": "@/lib",
    "hooks": "@/hooks"
  }
}
```

- [ ] **Step 7: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/package.json web/package-lock.json web/src/lib/utils.ts web/src/lib/utils.test.ts web/components.json
git commit -m "Add Base UI, Lucide and the cn helper for the design system"
```

---

### Task 2: Tokens, fonts and the contrast test

**Files:**
- Create: `web/src/app/theme.test.ts`
- Modify: `web/src/app/globals.css` (replace the whole file)
- Modify: `web/src/app/layout.tsx` (replace the whole file)

**Interfaces:**
- Produces Tailwind utilities that later tasks use:
  - **Colours** (`bg-`, `text-`, `border-`, `fill-`, `stroke-`, `outline-`):
    - neutrals: `background`, `foreground`, `sidebar`, `card`, `card-foreground`, `popover`, `popover-foreground`, `muted`, `muted-foreground`, `subtle-foreground`, `secondary`, `secondary-foreground`, `accent`, `accent-foreground`
    - borders and focus: `border`, `border-strong`, `input`, `ring`, `lightbox`
    - brand: `primary`, `primary-hover`, `primary-foreground`, `primary-soft`, `primary-strong`
    - status, each with `-strong` and `-soft`: `success`, `warning`, `destructive`, `info`
    - extra destructive tokens: `destructive-hover`, `destructive-foreground`
    - charts: `chart-1` to `chart-8`
  - **Text sizes:** `text-display-xl`, `text-display-lg`, `text-display-md`, `text-display-sm`, `text-small`, `text-eyebrow`. `text-sm` is 14/22 and `text-base` is 16/26.
  - **Fonts:** `font-sans` (Geist), `font-display` (Instrument Serif), `font-mono` (Geist Mono).
  - **Radius:** `rounded-sm` 6px, `rounded-md` 8px, `rounded-lg` 10px, `rounded-xl` 14px.
  - **Shadows:** `shadow-popover`, `shadow-overlay`.
  - **CSS variable:** `--scrim` for overlay backdrops (`bg-(--scrim)`).
  - **Utility:** `no-scrollbar`.

- [ ] **Step 1: Write the failing test**

Create `web/src/app/theme.test.ts`:

```ts
import { readFileSync } from "node:fs";

import { describe, expect, test } from "vitest";

const css = readFileSync(new URL("./globals.css", import.meta.url), "utf8");

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
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm --prefix web test -- src/app/theme.test.ts`
Expected: FAIL. Today's `globals.css` defines only two tokens.

- [ ] **Step 3: Replace `globals.css`**

Replace the whole of `web/src/app/globals.css` with:

```css
@import "tailwindcss";
@import "tw-animate-css";

:root {
  color-scheme: light;
  --background: #f7f3ed;
  --foreground: #1e1a15;
  --sidebar: #f0eadf;
  --card: #fffdf9;
  --popover: #fffdf9;
  --muted: #f3ede4;
  --secondary: #f3ede4;
  --accent: #ebe3d7;
  --muted-foreground: #574f45;
  --subtle-foreground: #6b6155;
  --border: #e6ddd0;
  --border-strong: #d8cdbc;
  --input: #887b6b;
  --ring: #a8461f;
  --lightbox: #ffffff;
  --primary: #a8461f;
  --primary-hover: #933c1a;
  --primary-foreground: #fffdf9;
  --primary-soft: #f5e3d8;
  --primary-strong: #8a3a19;
  --success: #2e7d4f;
  --success-strong: #2d6a42;
  --success-soft: #e4f0e7;
  --warning: #a27905;
  --warning-strong: #735600;
  --warning-soft: #fbf2d3;
  --destructive: #b22251;
  --destructive-hover: #9a1d46;
  --destructive-foreground: #fffdf9;
  --destructive-strong: #a21b4a;
  --destructive-soft: #fce8eb;
  --info: #3a64ac;
  --info-strong: #2d5390;
  --info-soft: #e7eef8;
  --chart-1: #c15529;
  --chart-2: #3b6bc2;
  --chart-3: #0d957e;
  --chart-4: #d09a21;
  --chart-5: #b2548d;
  --chart-6: #54882c;
  --chart-7: #6b5ac5;
  --chart-8: #ca4459;
  --scrim: rgb(30 26 21 / 0.42);
  --popover-shadow: 0 12px 32px -12px rgb(30 26 21 / 0.28), 0 2px 6px rgb(30 26 21 / 0.06);
  --overlay-shadow: 0 24px 56px -16px rgb(30 26 21 / 0.35);
}

@media (prefers-color-scheme: dark) {
  :root {
    color-scheme: dark;
    --background: #15130f;
    --foreground: #f2ebe0;
    --sidebar: #1a1713;
    --card: #1e1b17;
    --popover: #1e1b17;
    --muted: #25211c;
    --secondary: #25211c;
    --accent: #2d2822;
    --muted-foreground: #c4baab;
    --subtle-foreground: #a39887;
    --border: #322c25;
    --border-strong: #40382f;
    --input: #7d7264;
    --ring: #e8946c;
    --lightbox: #2a2a2a;
    --primary: #e8946c;
    --primary-hover: #eda584;
    --primary-foreground: #24110a;
    --primary-soft: #3a251b;
    --primary-strong: #f2b293;
    --success: #5db07f;
    --success-strong: #8fcfa6;
    --success-soft: #1c2a20;
    --warning: #d3a93a;
    --warning-strong: #e8c46a;
    --warning-soft: #2c2513;
    --destructive: #e8708a;
    --destructive-hover: #ee8ba0;
    --destructive-foreground: #2a0a12;
    --destructive-strong: #f29aae;
    --destructive-soft: #351a21;
    --info: #6f95d6;
    --info-strong: #a8c2ee;
    --info-soft: #1b2435;
    --chart-1: #d87248;
    --chart-2: #5988d7;
    --chart-3: #29a288;
    --chart-4: #b98c19;
    --chart-5: #c368a2;
    --chart-6: #65933a;
    --chart-7: #8375d8;
    --chart-8: #d2576d;
    --scrim: rgb(5 4 3 / 0.62);
    --popover-shadow: 0 16px 40px -12px rgb(0 0 0 / 0.65);
    --overlay-shadow: 0 28px 64px -16px rgb(0 0 0 / 0.7);
  }
}

@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-sidebar: var(--sidebar);
  --color-card: var(--card);
  --color-card-foreground: var(--foreground);
  --color-popover: var(--popover);
  --color-popover-foreground: var(--foreground);
  --color-muted: var(--muted);
  --color-muted-foreground: var(--muted-foreground);
  --color-subtle-foreground: var(--subtle-foreground);
  --color-secondary: var(--secondary);
  --color-secondary-foreground: var(--foreground);
  --color-accent: var(--accent);
  --color-accent-foreground: var(--foreground);
  --color-border: var(--border);
  --color-border-strong: var(--border-strong);
  --color-input: var(--input);
  --color-ring: var(--ring);
  --color-lightbox: var(--lightbox);
  --color-primary: var(--primary);
  --color-primary-hover: var(--primary-hover);
  --color-primary-foreground: var(--primary-foreground);
  --color-primary-soft: var(--primary-soft);
  --color-primary-strong: var(--primary-strong);
  --color-success: var(--success);
  --color-success-strong: var(--success-strong);
  --color-success-soft: var(--success-soft);
  --color-warning: var(--warning);
  --color-warning-strong: var(--warning-strong);
  --color-warning-soft: var(--warning-soft);
  --color-destructive: var(--destructive);
  --color-destructive-hover: var(--destructive-hover);
  --color-destructive-foreground: var(--destructive-foreground);
  --color-destructive-strong: var(--destructive-strong);
  --color-destructive-soft: var(--destructive-soft);
  --color-info: var(--info);
  --color-info-strong: var(--info-strong);
  --color-info-soft: var(--info-soft);
  --color-chart-1: var(--chart-1);
  --color-chart-2: var(--chart-2);
  --color-chart-3: var(--chart-3);
  --color-chart-4: var(--chart-4);
  --color-chart-5: var(--chart-5);
  --color-chart-6: var(--chart-6);
  --color-chart-7: var(--chart-7);
  --color-chart-8: var(--chart-8);
  --font-sans: var(--font-geist-sans), ui-sans-serif, system-ui, sans-serif;
  --font-display: var(--font-instrument-serif), ui-serif, Georgia, serif;
  --font-mono: var(--font-geist-mono), ui-monospace, monospace;
  --shadow-popover: var(--popover-shadow);
  --shadow-overlay: var(--overlay-shadow);
}

@theme {
  --radius-sm: 0.375rem;
  --radius-md: 0.5rem;
  --radius-lg: 0.625rem;
  --radius-xl: 0.875rem;
  --text-sm--line-height: calc(1.375 / 0.875);
  --text-base--line-height: calc(1.625 / 1);
  --text-small: 0.8125rem;
  --text-small--line-height: calc(1.25 / 0.8125);
  --text-eyebrow: 0.6875rem;
  --text-eyebrow--line-height: calc(1 / 0.6875);
  --text-eyebrow--letter-spacing: 0.1em;
  --text-eyebrow--font-weight: 600;
  --text-display-sm: 1.375rem;
  --text-display-sm--line-height: calc(1.75 / 1.375);
  --text-display-md: 1.75rem;
  --text-display-md--line-height: calc(2.125 / 1.75);
  --text-display-lg: 2.375rem;
  --text-display-lg--line-height: calc(2.625 / 2.375);
  --text-display-xl: 3rem;
  --text-display-xl--line-height: calc(3.25 / 3);
}

@utility no-scrollbar {
  scrollbar-width: none;

  &::-webkit-scrollbar {
    display: none;
  }
}

@layer base {
  *,
  ::after,
  ::before {
    border-color: var(--color-border);
  }

  body {
    background-color: var(--color-background);
    color: var(--color-foreground);
    font-family: var(--font-sans);
    -webkit-font-smoothing: antialiased;
    -moz-osx-font-smoothing: grayscale;
  }

  ::selection {
    background-color: var(--color-primary-soft);
    color: var(--color-foreground);
  }

  :focus-visible {
    outline: 2px solid var(--color-ring);
    outline-offset: 2px;
  }

  svg.lucide {
    stroke-width: 1.75;
  }
}

@media (prefers-reduced-motion: reduce) {
  *,
  ::after,
  ::before {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `npm --prefix web test -- src/app/theme.test.ts`
Expected: PASS. Every pair in both themes clears its threshold; the values were computed during design.

- [ ] **Step 5: Load the fonts in the root layout**

Replace the whole of `web/src/app/layout.tsx` with:

```tsx
import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono, Instrument_Serif } from "next/font/google";

import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

const instrumentSerif = Instrument_Serif({
  variable: "--font-instrument-serif",
  subsets: ["latin"],
  weight: "400",
  style: ["normal", "italic"],
});

export const metadata: Metadata = {
  title: { default: "Kairo", template: "%s · Kairo" },
  description:
    "From a rising demand signal to a live, on-brand Google Ads campaign, with proof of the lift it caused.",
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#f7f3ed" },
    { media: "(prefers-color-scheme: dark)", color: "#15130f" },
  ],
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} ${instrumentSerif.variable} h-full`}>
      <body className="min-h-full">{children}</body>
    </html>
  );
}
```

- [ ] **Step 6: Check that the build compiles the new CSS**

Run: `npm --prefix web run build`
Expected: the build succeeds. A failure that mentions `tw-animate-css` or `@theme` means the CSS above was mistyped.

- [ ] **Step 7: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/app/globals.css web/src/app/layout.tsx web/src/app/theme.test.ts
git commit -m "Define the Warm Editorial tokens, the fonts and a contrast test"
```

---

### Task 3: Form primitives and form feedback

**Files:**
- Create: `web/src/components/ui/button.tsx`, `input.tsx`, `native-select.tsx`, `checkbox.tsx`, `choice-chip.tsx`, `label.tsx`, `field.tsx`
- Create: `web/src/components/ui/button.test.tsx`, `web/src/components/ui/form-controls.test.tsx`
- Modify: `web/src/components/field-error.tsx`, `web/src/components/form-status.tsx`, `web/src/components/role-checkboxes.tsx` (replace each file)

**Interfaces:**
- Produces:
  - `Button(props: ComponentProps<"button"> & { variant?: "default" | "secondary" | "ghost" | "destructive" | "destructive-solid" | "link"; size?: "sm" | "default" | "lg" | "icon"; pending?: boolean })`. Its `type` defaults to `"button"`.
  - `buttonVariants({ variant, size })`, which returns a class string so links can look like buttons.
  - `Input(props: ComponentProps<"input">)`
  - `NativeSelect(props: ComponentProps<"select">)`. `className` styles the wrapper; every other prop goes to `<select>`.
  - `Checkbox(props: Omit<ComponentProps<"input">, "type" | "className"> & { className?: string })`
  - `ChoiceChip(props: Omit<ComponentProps<"input">, "type" | "className"> & { className?: string; children: ReactNode })`
  - `Label(props: ComponentProps<"label">)`
  - `Field(props: ComponentProps<"div">)` and `FieldDescription(props: ComponentProps<"p">)`
  - `FieldError({ id, errors })`, `FormStatus({ state })` and `RoleCheckboxes({ legend, defaultValue })`, each keeping its current props

- [ ] **Step 1: Write the failing tests**

Create `web/src/components/ui/button.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { Button, buttonVariants } from "./button";

test("is a plain button unless told otherwise, so it never submits a form by accident", () => {
  render(<Button>Save</Button>);

  expect(screen.getByRole("button", { name: "Save" }).getAttribute("type")).toBe("button");
});

test("keeps its label and blocks clicks while pending", () => {
  render(
    <Button type="submit" pending>
      Save changes
    </Button>,
  );

  const button = screen.getByRole("button", { name: "Save changes" });
  expect(button.matches(":disabled")).toBe(true);
  expect(button.getAttribute("aria-busy")).toBe("true");
});

test("styles a link as a button", () => {
  render(
    <a href="/" className={buttonVariants({ variant: "secondary" })}>
      Go to Kairo
    </a>,
  );

  expect(screen.getByRole("link", { name: "Go to Kairo" }).className).toContain("border-border-strong");
});
```

Create `web/src/components/ui/form-controls.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";

import { Checkbox } from "./checkbox";
import { ChoiceChip } from "./choice-chip";
import { NativeSelect } from "./native-select";

test("a native select is found by its label and keeps its value", () => {
  render(
    <>
      <label htmlFor="currency">Currency</label>
      <NativeSelect id="currency" name="currency" defaultValue="INR">
        <option value="USD">USD</option>
        <option value="INR">INR</option>
      </NativeSelect>
    </>,
  );

  expect((screen.getByLabelText("Currency") as HTMLSelectElement).value).toBe("INR");
});

test("a checkbox submits its value only when checked", () => {
  render(
    <form data-testid="form">
      <label>
        <Checkbox name="duty" value="u1" defaultChecked />
        Neha Rao
      </label>
      <label>
        <Checkbox name="duty" value="u2" />
        Arjun Mehta
      </label>
    </form>,
  );

  fireEvent.click(screen.getByLabelText("Arjun Mehta"));

  expect(new FormData(screen.getByTestId("form") as HTMLFormElement).getAll("duty")).toEqual(["u1", "u2"]);
});

test("a choice chip is named by its text and toggles", () => {
  render(
    <ChoiceChip name="roles" value="approver">
      Approver
    </ChoiceChip>,
  );

  const chip = screen.getByLabelText("Approver") as HTMLInputElement;
  fireEvent.click(chip);

  expect(chip.checked).toBe(true);
});

test("role checkboxes send every chosen role as a named group", () => {
  render(
    <form data-testid="form">
      <RoleCheckboxes legend="Roles for the new member" defaultValue={["marketer"]} />
    </form>,
  );

  fireEvent.click(screen.getByLabelText("Approver"));

  expect(screen.getByRole("group", { name: "Roles for the new member" })).toBeTruthy();
  expect(new FormData(screen.getByTestId("form") as HTMLFormElement).getAll("roles")).toEqual(["marketer", "approver"]);
});

test("form status announces success politely and errors assertively", () => {
  const { rerender } = render(<FormStatus state={{ status: "saved", message: "Saved." }} />);
  expect(screen.getByRole("status").textContent).toBe("Saved.");

  rerender(<FormStatus state={{ status: "error", message: "Something went wrong. Please try again." }} />);
  expect(screen.getByRole("alert").textContent).toBe("Something went wrong. Please try again.");
});

test("field errors read as one sentence under their field", () => {
  render(<FieldError id="email-error" errors={["Enter an email.", "It must be unique."]} />);

  expect(screen.getByText("Enter an email. It must be unique.").closest("p")?.id).toBe("email-error");
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- src/components/ui`
Expected: FAIL, because the `ui` modules don't exist.

- [ ] **Step 3: Write the primitives**

Create `web/src/components/ui/button.tsx`:

```tsx
import { cva, type VariantProps } from "class-variance-authority";
import { LoaderCircle } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-md border border-transparent text-sm font-semibold whitespace-nowrap transition-colors duration-120 select-none disabled:pointer-events-none disabled:opacity-55 aria-busy:cursor-progress [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-primary-hover",
        secondary: "border-border-strong bg-card text-foreground hover:bg-accent",
        ghost: "text-muted-foreground hover:bg-accent hover:text-foreground",
        destructive: "text-destructive hover:bg-destructive-soft",
        "destructive-solid": "bg-destructive text-destructive-foreground hover:bg-destructive-hover",
        link: "text-primary underline-offset-4 hover:underline",
      },
      size: {
        sm: "h-8 px-3 text-small pointer-coarse:h-11",
        default: "h-9 px-4 pointer-coarse:h-11",
        lg: "h-10 px-5 pointer-coarse:h-11",
        icon: "size-9 pointer-coarse:size-11",
      },
    },
    compoundVariants: [{ variant: "link", class: "h-auto px-0 pointer-coarse:h-auto" }],
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

type ButtonProps = ComponentProps<"button"> & VariantProps<typeof buttonVariants> & { pending?: boolean };

function Button({ className, variant, size, pending = false, disabled, type = "button", children, ...props }: ButtonProps) {
  return (
    <button
      type={type}
      data-slot="button"
      className={cn(buttonVariants({ variant, size }), className)}
      disabled={disabled || pending}
      aria-busy={pending || undefined}
      {...props}
    >
      {pending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
      {children}
    </button>
  );
}

export { Button, buttonVariants };
```

Create `web/src/components/ui/input.tsx`:

```tsx
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Input({ className, type = "text", ...props }: ComponentProps<"input">) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        "h-9 w-full min-w-0 rounded-md border border-input bg-card px-3 text-base text-foreground transition-colors duration-120 placeholder:text-subtle-foreground disabled:cursor-not-allowed disabled:bg-muted disabled:text-muted-foreground aria-invalid:border-destructive sm:text-sm pointer-coarse:h-11",
        className,
      )}
      {...props}
    />
  );
}

export { Input };
```

Create `web/src/components/ui/native-select.tsx`:

```tsx
import { ChevronDown } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function NativeSelect({ className, ...props }: ComponentProps<"select">) {
  return (
    <div data-slot="native-select" className={cn("relative w-full", className)}>
      <select
        className="h-9 w-full min-w-0 cursor-pointer appearance-none rounded-md border border-input bg-card py-0 pr-9 pl-3 text-base text-foreground transition-colors duration-120 disabled:cursor-not-allowed disabled:bg-muted disabled:text-muted-foreground aria-invalid:border-destructive sm:text-sm pointer-coarse:h-11"
        {...props}
      />
      <ChevronDown
        className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-subtle-foreground"
        aria-hidden="true"
      />
    </div>
  );
}

export { NativeSelect };
```

Create `web/src/components/ui/checkbox.tsx`:

```tsx
import { Check } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

type CheckboxProps = Omit<ComponentProps<"input">, "type" | "className"> & { className?: string };

function Checkbox({ className, ...props }: CheckboxProps) {
  return (
    <span data-slot="checkbox" className={cn("relative inline-grid size-4 shrink-0 place-items-center", className)}>
      <input
        type="checkbox"
        className="peer size-4 cursor-pointer appearance-none rounded-[4px] border border-input bg-card transition-colors duration-120 checked:border-primary checked:bg-primary disabled:cursor-not-allowed disabled:opacity-60"
        {...props}
      />
      <Check
        className="pointer-events-none absolute size-3 text-primary-foreground opacity-0 [stroke-width:3] peer-checked:opacity-100"
        aria-hidden="true"
      />
    </span>
  );
}

export { Checkbox };
```

Create `web/src/components/ui/choice-chip.tsx`:

```tsx
import type { ComponentProps, ReactNode } from "react";

import { cn } from "@/lib/utils";

import { Checkbox } from "./checkbox";

type ChoiceChipProps = Omit<ComponentProps<"input">, "type" | "className"> & { className?: string; children: ReactNode };

function ChoiceChip({ className, children, ...props }: ChoiceChipProps) {
  return (
    <label
      data-slot="choice-chip"
      className={cn(
        "inline-flex h-8 cursor-pointer items-center gap-2 rounded-full border border-border-strong bg-card pr-3 pl-2 text-small font-medium text-muted-foreground transition-colors duration-120 select-none hover:bg-accent has-checked:border-primary/40 has-checked:bg-primary-soft has-checked:text-primary-strong has-disabled:cursor-not-allowed has-disabled:opacity-60 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-ring pointer-coarse:h-11",
        className,
      )}
    >
      <Checkbox className="[&>input]:focus-visible:outline-none" {...props} />
      {children}
    </label>
  );
}

export { ChoiceChip };
```

Create `web/src/components/ui/label.tsx`:

```tsx
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Label({ className, ...props }: ComponentProps<"label">) {
  return <label data-slot="label" className={cn("text-small font-semibold text-foreground", className)} {...props} />;
}

export { Label };
```

Create `web/src/components/ui/field.tsx`:

```tsx
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Field({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="field" className={cn("flex min-w-0 flex-col gap-1.5", className)} {...props} />;
}

function FieldDescription({ className, ...props }: ComponentProps<"p">) {
  return <p data-slot="field-description" className={cn("text-small text-subtle-foreground", className)} {...props} />;
}

export { Field, FieldDescription };
```

- [ ] **Step 4: Restyle the form feedback and role chips**

Replace `web/src/components/field-error.tsx`:

```tsx
import { CircleAlert } from "lucide-react";

export function FieldError({ id, errors }: { id: string; errors?: string[] }) {
  if (!errors?.length) {
    return null;
  }

  return (
    <p id={id} className="flex items-start gap-1.5 text-small text-destructive-strong">
      <CircleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      <span>{errors.join(" ")}</span>
    </p>
  );
}
```

Replace `web/src/components/form-status.tsx`:

```tsx
import { CircleAlert, CircleCheck } from "lucide-react";

import type { FormState } from "@/lib/forms";
import { cn } from "@/lib/utils";

export function FormStatus({ state }: { state: FormState }) {
  if (!state.message) {
    return null;
  }

  const failed = state.status === "error";
  const Icon = failed ? CircleAlert : CircleCheck;

  return (
    <p
      role={failed ? "alert" : "status"}
      className={cn("flex items-start gap-2 text-small", failed ? "text-destructive-strong" : "text-success-strong")}
    >
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span>{state.message}</span>
    </p>
  );
}
```

Replace `web/src/components/role-checkboxes.tsx`:

```tsx
import { ChoiceChip } from "@/components/ui/choice-chip";
import { roleLabels, type Role } from "@/lib/core-api/types";

const roles: Role[] = ["admin", "marketer", "approver"];

type RoleCheckboxesProps = {
  legend: string;
  defaultValue?: Role[];
};

export function RoleCheckboxes({ legend, defaultValue = [] }: RoleCheckboxesProps) {
  return (
    <fieldset className="flex min-w-0 flex-wrap gap-2">
      <legend className="sr-only">{legend}</legend>
      {roles.map((role) => (
        <ChoiceChip key={role} name="roles" value={role} defaultChecked={defaultValue.includes(role)}>
          {roleLabels[role]}
        </ChoiceChip>
      ))}
    </fieldset>
  );
}
```

- [ ] **Step 5: Run the new tests and watch them pass**

Run: `npm --prefix web test -- src/components/ui`
Expected: PASS, 9 tests.

- [ ] **Step 6: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS. The existing form tests still find "Saved.", "Approver" and the other labels.

```bash
git add web/src/components/ui web/src/components/field-error.tsx web/src/components/form-status.tsx web/src/components/role-checkboxes.tsx
git commit -m "Add the form primitives and restyle form feedback and role chips"
```

---

### Task 4: Display primitives and initials

**Files:**
- Create: `web/src/components/ui/card.tsx`, `badge.tsx`, `alert.tsx`, `avatar.tsx`, `skeleton.tsx`, `table.tsx`
- Create: `web/src/lib/initials.ts`, `web/src/lib/initials.test.ts`
- Create: `web/src/components/user-avatar.tsx`
- Create: `web/src/components/ui/display.test.tsx`

**Interfaces:**
- Produces:
  - **Card:** `Card`, `CardHeader`, `CardTitle` (`level?: 2 | 3`, default 2), `CardDescription`, `CardContent`, `CardFooter`
  - **Badge:** `Badge({ variant?: "neutral" | "gate" | "success" | "warning" | "destructive" | "info" })`. Status variants render their icon.
  - **Alert:** `Alert({ variant?: "neutral" | "info" | "success" | "warning" | "destructive", role? })`. Only `destructive` defaults to `role="alert"`.
  - **Avatar:** `Avatar`, `AvatarImage`, `AvatarFallback`, wrapping Base UI and taking `className?: string`
  - **Skeleton:** `Skeleton`
  - **Table:** `Table`, `TableHeader`, `TableBody`, `TableRow`, `TableHead`, `TableCell`, `TableCaption` (screen-reader only)
  - **Initials:** `initials(name: string): string`
  - **UserAvatar:** `UserAvatar({ name: string; pictureUrl?: string | null; isYou?: boolean; className?: string })`, hidden from assistive technology

- [ ] **Step 1: Write the failing tests**

Create `web/src/lib/initials.test.ts`:

```ts
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
```

Create `web/src/components/ui/display.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { UserAvatar } from "@/components/user-avatar";

import { Alert } from "./alert";
import { Badge } from "./badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./table";

test("status badges pair a hidden icon with their words", () => {
  render(<Badge variant="success">Approved</Badge>);

  expect(screen.getByText("Approved").querySelector("svg")?.getAttribute("aria-hidden")).toBe("true");
});

test("neutral and gate badges are words only", () => {
  render(
    <>
      <Badge>Draft</Badge>
      <Badge variant="gate">Gate 1 · Brief</Badge>
    </>,
  );

  expect(screen.getByText("Draft").querySelector("svg")).toBeNull();
  expect(screen.getByText("Gate 1 · Brief").querySelector("svg")).toBeNull();
});

test("only a destructive alert interrupts", () => {
  render(
    <>
      <Alert variant="destructive">Launch failed.</Alert>
      <Alert>Only admins can see the audit log.</Alert>
    </>,
  );

  expect(screen.getAllByRole("alert").map((alert) => alert.textContent)).toEqual(["Launch failed."]);
});

test("an avatar without a photo shows initials and stays out of the accessibility tree", () => {
  render(<UserAvatar name="Priya Shah" pictureUrl={null} />);

  expect(screen.getByText("PS").closest("[aria-hidden='true']")).not.toBeNull();
});

test("tables keep their header semantics", () => {
  render(
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>When</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell>Today</TableCell>
        </TableRow>
      </TableBody>
    </Table>,
  );

  expect(screen.getByRole("columnheader", { name: "When" })).toBeTruthy();
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- src/lib/initials.test.ts src/components/ui/display.test.tsx`
Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Write `initials` and the avatar**

Create `web/src/lib/initials.ts`:

```ts
const graphemes = new Intl.Segmenter(undefined, { granularity: "grapheme" });

function firstGrapheme(word: string) {
  const first = graphemes.segment(word)[Symbol.iterator]().next();
  return first.done ? "" : first.value.segment;
}

export function initials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return "?";
  }

  const letters = words.length === 1 ? firstGrapheme(words[0]) : firstGrapheme(words[0]) + firstGrapheme(words[words.length - 1]);
  return letters.toLocaleUpperCase();
}
```

Create `web/src/components/ui/avatar.tsx`:

```tsx
"use client";

import { Avatar as AvatarPrimitive } from "@base-ui/react/avatar";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

function Avatar({ className, ...props }: WithClassName<AvatarPrimitive.Root.Props>) {
  return (
    <AvatarPrimitive.Root
      data-slot="avatar"
      className={cn("relative inline-flex size-8 shrink-0 overflow-hidden rounded-full bg-accent align-middle select-none", className)}
      {...props}
    />
  );
}

function AvatarImage({ className, ...props }: WithClassName<AvatarPrimitive.Image.Props>) {
  return <AvatarPrimitive.Image data-slot="avatar-image" className={cn("size-full object-cover", className)} {...props} />;
}

function AvatarFallback({ className, ...props }: WithClassName<AvatarPrimitive.Fallback.Props>) {
  return (
    <AvatarPrimitive.Fallback
      data-slot="avatar-fallback"
      className={cn("flex size-full items-center justify-center text-xs font-semibold text-muted-foreground", className)}
      {...props}
    />
  );
}

export { Avatar, AvatarFallback, AvatarImage };
```

Create `web/src/components/user-avatar.tsx`:

```tsx
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { initials } from "@/lib/initials";
import { cn } from "@/lib/utils";

type UserAvatarProps = {
  name: string;
  pictureUrl?: string | null;
  isYou?: boolean;
  className?: string;
};

export function UserAvatar({ name, pictureUrl, isYou = false, className }: UserAvatarProps) {
  return (
    <Avatar aria-hidden="true" className={cn(isYou && "bg-primary-soft", className)}>
      {pictureUrl && <AvatarImage src={pictureUrl} alt="" referrerPolicy="no-referrer" />}
      <AvatarFallback className={cn(isYou && "text-primary-strong")}>{initials(name)}</AvatarFallback>
    </Avatar>
  );
}
```

- [ ] **Step 4: Write the other display primitives**

Create `web/src/components/ui/card.tsx`:

```tsx
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Card({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      data-slot="card"
      className={cn("flex flex-col gap-4 rounded-lg border border-border bg-card py-4 text-card-foreground md:gap-5 md:py-5", className)}
      {...props}
    />
  );
}

function CardHeader({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="card-header" className={cn("flex flex-col gap-1 px-4 md:px-5", className)} {...props} />;
}

function CardTitle({ className, level = 2, ...props }: ComponentProps<"h2"> & { level?: 2 | 3 }) {
  const Heading = level === 3 ? "h3" : "h2";
  return <Heading data-slot="card-title" className={cn("text-base font-semibold text-foreground", className)} {...props} />;
}

function CardDescription({ className, ...props }: ComponentProps<"p">) {
  return <p data-slot="card-description" className={cn("text-small text-subtle-foreground", className)} {...props} />;
}

function CardContent({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="card-content" className={cn("px-4 md:px-5", className)} {...props} />;
}

function CardFooter({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      data-slot="card-footer"
      className={cn("flex flex-wrap items-center gap-3 border-t border-border px-4 pt-4 md:px-5 md:pt-5", className)}
      {...props}
    />
  );
}

export { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle };
```

Create `web/src/components/ui/badge.tsx`:

```tsx
import { cva, type VariantProps } from "class-variance-authority";
import { CircleCheck, CircleX, Info, TriangleAlert } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

const badgeVariants = cva(
  "inline-flex h-6 w-fit shrink-0 items-center gap-1.5 rounded-full px-2.5 text-xs font-medium whitespace-nowrap [&_svg]:size-3.5 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        neutral: "bg-accent text-muted-foreground",
        gate: "bg-primary-soft text-primary-strong",
        success: "bg-success-soft text-success-strong",
        warning: "bg-warning-soft text-warning-strong",
        destructive: "bg-destructive-soft text-destructive-strong",
        info: "bg-info-soft text-info-strong",
      },
    },
    defaultVariants: {
      variant: "neutral",
    },
  },
);

const statusIcons = {
  success: CircleCheck,
  warning: TriangleAlert,
  destructive: CircleX,
  info: Info,
};

function Badge({ className, variant = "neutral", children, ...props }: ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  const Icon = variant && variant in statusIcons ? statusIcons[variant as keyof typeof statusIcons] : null;

  return (
    <span data-slot="badge" className={cn(badgeVariants({ variant }), className)} {...props}>
      {Icon && <Icon aria-hidden="true" />}
      {children}
    </span>
  );
}

export { Badge, badgeVariants };
```

Create `web/src/components/ui/alert.tsx`:

```tsx
import { cva, type VariantProps } from "class-variance-authority";
import { CircleAlert, CircleCheck, Info, TriangleAlert } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

const alertVariants = cva(
  "grid w-full grid-cols-[auto_minmax(0,1fr)] items-start gap-x-3 rounded-lg border px-4 py-3 text-sm [&>svg]:mt-0.5 [&>svg]:size-4",
  {
    variants: {
      variant: {
        neutral: "border-border bg-muted text-foreground [&>svg]:text-muted-foreground",
        info: "border-info/25 bg-info-soft text-info-strong [&>svg]:text-info",
        success: "border-success/25 bg-success-soft text-success-strong [&>svg]:text-success",
        warning: "border-warning/30 bg-warning-soft text-warning-strong [&>svg]:text-warning",
        destructive: "border-destructive/25 bg-destructive-soft text-destructive-strong [&>svg]:text-destructive",
      },
    },
    defaultVariants: {
      variant: "neutral",
    },
  },
);

const alertIcons = {
  neutral: Info,
  info: Info,
  success: CircleCheck,
  warning: TriangleAlert,
  destructive: CircleAlert,
};

function Alert({ className, variant = "neutral", role, children, ...props }: ComponentProps<"div"> & VariantProps<typeof alertVariants>) {
  const Icon = alertIcons[variant ?? "neutral"];

  return (
    <div
      data-slot="alert"
      role={role ?? (variant === "destructive" ? "alert" : undefined)}
      className={cn(alertVariants({ variant }), className)}
      {...props}
    >
      <Icon aria-hidden="true" />
      <div className="flex min-w-0 flex-col gap-0.5">{children}</div>
    </div>
  );
}

export { Alert };
```

Create `web/src/components/ui/skeleton.tsx`:

```tsx
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Skeleton({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="skeleton" className={cn("animate-pulse rounded-md bg-accent", className)} {...props} />;
}

export { Skeleton };
```

Create `web/src/components/ui/table.tsx`:

```tsx
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Table({ className, ...props }: ComponentProps<"table">) {
  return (
    <div data-slot="table-container" className="relative w-full overflow-x-auto">
      <table data-slot="table" className={cn("w-full caption-bottom text-sm", className)} {...props} />
    </div>
  );
}

function TableHeader({ className, ...props }: ComponentProps<"thead">) {
  return <thead data-slot="table-header" className={cn("[&_tr]:border-b", className)} {...props} />;
}

function TableBody({ className, ...props }: ComponentProps<"tbody">) {
  return <tbody data-slot="table-body" className={cn("[&_tr:last-child]:border-0", className)} {...props} />;
}

function TableRow({ className, ...props }: ComponentProps<"tr">) {
  return <tr data-slot="table-row" className={cn("border-b border-border", className)} {...props} />;
}

function TableHead({ className, ...props }: ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn("h-10 px-4 text-left align-middle text-xs font-medium whitespace-nowrap text-subtle-foreground md:px-5", className)}
      {...props}
    />
  );
}

function TableCell({ className, ...props }: ComponentProps<"td">) {
  return <td data-slot="table-cell" className={cn("px-4 py-3 align-top md:px-5", className)} {...props} />;
}

function TableCaption({ className, ...props }: ComponentProps<"caption">) {
  return <caption data-slot="table-caption" className={cn("sr-only", className)} {...props} />;
}

export { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow };
```

- [ ] **Step 5: Run the new tests and watch them pass**

Run: `npm --prefix web test -- src/lib/initials.test.ts src/components/ui/display.test.tsx`
Expected: PASS, 11 tests.

- [ ] **Step 6: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/components/ui web/src/lib/initials.ts web/src/lib/initials.test.ts web/src/components/user-avatar.tsx
git commit -m "Add the display primitives and avatar initials"
```

---

### Task 5: Menus, sheets and confirmation dialogs

**Files:**
- Create: `web/src/components/ui/dropdown-menu.tsx`, `sheet.tsx`, `alert-dialog.tsx`
- Create: `web/src/components/confirm-dialog.tsx`
- Create: `web/src/components/ui/overlays.test.tsx`, `web/src/components/confirm-dialog.test.tsx`

**Interfaces:**
- Consumes: `Button` and `buttonVariants` (Task 3), `cn` (Task 1).
- Produces:
  - **Menus:** `DropdownMenu`, `DropdownMenuTrigger`, `DropdownMenuContent` (`align`, `side`, `sideOffset`, `aria-label`), `DropdownMenuGroup`, `DropdownMenuLabel`, `DropdownMenuItem` (`onClick`), `DropdownMenuLinkItem` (`href` or `render={<Link href />}`), `DropdownMenuSeparator`
  - **Sheets:** `Sheet` (`open`, `onOpenChange`), `SheetTrigger`, `SheetContent` (`side?: "left" | "right"`, default `"left"`, with a built-in "Close" button), `SheetTitle`
  - **Dialog parts:** `AlertDialog`, `AlertDialogTrigger`, `AlertDialogContent` (`initialFocus`), `AlertDialogHeader`, `AlertDialogFooter`, `AlertDialogTitle`, `AlertDialogDescription`, `AlertDialogCancel`
  - **ConfirmDialog:** `ConfirmDialog({ trigger: ReactElement; title: string; description: string; confirmLabel: string; action: (formData: FormData) => void | Promise<void> })`

- [ ] **Step 1: Write the failing tests**

Create `web/src/components/ui/overlays.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { DropdownMenu, DropdownMenuContent, DropdownMenuLinkItem, DropdownMenuTrigger } from "./dropdown-menu";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "./sheet";

test("a dropdown menu opens from its trigger, lists link items and closes on Escape", async () => {
  render(
    <DropdownMenu>
      <DropdownMenuTrigger>Acme Shoes</DropdownMenuTrigger>
      <DropdownMenuContent aria-label="Workspaces">
        <DropdownMenuLinkItem href="#acme" aria-current="page">
          Acme Shoes
        </DropdownMenuLinkItem>
        <DropdownMenuLinkItem href="#beta">Beta Bags</DropdownMenuLinkItem>
      </DropdownMenuContent>
    </DropdownMenu>,
  );

  const trigger = screen.getByRole("button", { name: "Acme Shoes" });
  fireEvent.click(trigger);
  const menu = await screen.findByRole("menu", { name: "Workspaces" });

  expect(within(menu).getByRole("menuitem", { name: "Acme Shoes" }).getAttribute("aria-current")).toBe("page");
  expect(within(menu).getByRole("menuitem", { name: "Beta Bags" }).getAttribute("href")).toBe("#beta");

  fireEvent.keyDown(menu, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("menu")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});

test("a sheet is a named dialog that holds focus, closes on Escape and returns focus", async () => {
  render(
    <Sheet>
      <SheetTrigger aria-label="Open navigation">Menu</SheetTrigger>
      <SheetContent side="left">
        <SheetTitle>Navigation</SheetTitle>
        <a href="#today">Today</a>
      </SheetContent>
    </Sheet>,
  );

  const trigger = screen.getByRole("button", { name: "Open navigation" });
  fireEvent.click(trigger);
  const sheet = await screen.findByRole("dialog", { name: "Navigation" });

  await waitFor(() => expect(sheet.contains(document.activeElement)).toBe(true));
  expect(within(sheet).getByRole("button", { name: "Close" })).toBeTruthy();

  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});
```

Create `web/src/components/confirm-dialog.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";

function renderDialog(action = vi.fn()) {
  render(
    <ConfirmDialog
      trigger={<Button variant="destructive">Remove</Button>}
      title="Remove Priya Shah?"
      description="Priya Shah loses access to this workspace straight away."
      confirmLabel="Remove"
      action={action}
    />,
  );
  return action;
}

test("asks first, starting on Cancel, and does nothing when cancelled", async () => {
  const action = renderDialog();

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  const cancel = within(dialog).getByRole("button", { name: "Cancel" });

  await waitFor(() => expect(document.activeElement).toBe(cancel));
  fireEvent.click(cancel);

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(action).not.toHaveBeenCalled();
});

test("runs the action when confirmed, then closes", async () => {
  const action = renderDialog();

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove" }));

  await waitFor(() => expect(action).toHaveBeenCalledWith(expect.any(FormData)));
  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- src/components/ui/overlays.test.tsx src/components/confirm-dialog.test.tsx`
Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Write the dropdown menu**

Create `web/src/components/ui/dropdown-menu.tsx`:

```tsx
"use client";

import { Menu as MenuPrimitive } from "@base-ui/react/menu";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

const itemClassName =
  "relative flex min-h-9 cursor-default items-center gap-2.5 rounded-md px-2.5 text-sm text-foreground outline-none select-none data-disabled:pointer-events-none data-disabled:opacity-50 data-highlighted:bg-accent pointer-coarse:min-h-11 [&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-subtle-foreground";

function DropdownMenu(props: MenuPrimitive.Root.Props) {
  return <MenuPrimitive.Root {...props} />;
}

function DropdownMenuTrigger({ className, ...props }: WithClassName<MenuPrimitive.Trigger.Props>) {
  return <MenuPrimitive.Trigger data-slot="dropdown-menu-trigger" className={className} {...props} />;
}

function DropdownMenuContent({
  align = "start",
  side = "bottom",
  sideOffset = 6,
  className,
  ...props
}: WithClassName<MenuPrimitive.Popup.Props> & Pick<MenuPrimitive.Positioner.Props, "align" | "side" | "sideOffset">) {
  return (
    <MenuPrimitive.Portal>
      <MenuPrimitive.Positioner className="z-50 outline-none" align={align} side={side} sideOffset={sideOffset}>
        <MenuPrimitive.Popup
          data-slot="dropdown-menu-content"
          className={cn(
            "max-h-(--available-height) min-w-(--anchor-width) origin-(--transform-origin) overflow-y-auto rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-popover outline-none data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-open:duration-180 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-closed:duration-120",
            className,
          )}
          {...props}
        />
      </MenuPrimitive.Positioner>
    </MenuPrimitive.Portal>
  );
}

function DropdownMenuGroup(props: MenuPrimitive.Group.Props) {
  return <MenuPrimitive.Group {...props} />;
}

function DropdownMenuLabel({ className, ...props }: WithClassName<MenuPrimitive.GroupLabel.Props>) {
  return (
    <MenuPrimitive.GroupLabel
      data-slot="dropdown-menu-label"
      className={cn("px-2.5 pt-2 pb-1 text-xs font-medium text-subtle-foreground", className)}
      {...props}
    />
  );
}

function DropdownMenuItem({ className, ...props }: WithClassName<MenuPrimitive.Item.Props>) {
  return <MenuPrimitive.Item data-slot="dropdown-menu-item" className={cn(itemClassName, className)} {...props} />;
}

function DropdownMenuLinkItem({ className, ...props }: WithClassName<ComponentProps<typeof MenuPrimitive.LinkItem>>) {
  return <MenuPrimitive.LinkItem data-slot="dropdown-menu-link-item" className={cn(itemClassName, className)} {...props} />;
}

function DropdownMenuSeparator({ className, ...props }: WithClassName<MenuPrimitive.Separator.Props>) {
  return <MenuPrimitive.Separator className={cn("-mx-1 my-1 h-px bg-border", className)} {...props} />;
}

export {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuLinkItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
};
```

- [ ] **Step 4: Write the sheet**

Create `web/src/components/ui/sheet.tsx`:

```tsx
"use client";

import { Dialog as SheetPrimitive } from "@base-ui/react/dialog";
import { X } from "lucide-react";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

function Sheet(props: SheetPrimitive.Root.Props) {
  return <SheetPrimitive.Root {...props} />;
}

function SheetTrigger({ className, ...props }: WithClassName<SheetPrimitive.Trigger.Props>) {
  return <SheetPrimitive.Trigger data-slot="sheet-trigger" className={className} {...props} />;
}

function SheetContent({
  className,
  children,
  side = "left",
  ...props
}: WithClassName<SheetPrimitive.Popup.Props> & { side?: "left" | "right" }) {
  return (
    <SheetPrimitive.Portal>
      <SheetPrimitive.Backdrop className="fixed inset-0 z-50 bg-(--scrim) transition-opacity duration-240 data-ending-style:opacity-0 data-ending-style:duration-160 data-starting-style:opacity-0" />
      <SheetPrimitive.Popup
        data-slot="sheet-content"
        data-side={side}
        className={cn(
          "fixed inset-y-0 z-50 flex h-dvh w-[min(85vw,20rem)] flex-col border-border bg-card text-card-foreground shadow-overlay outline-none transition-transform duration-240 ease-[cubic-bezier(0.2,0,0,1)] data-ending-style:duration-160 data-ending-style:ease-[cubic-bezier(0.4,0,1,1)] data-[side=left]:left-0 data-[side=left]:border-r data-[side=left]:data-ending-style:-translate-x-full data-[side=left]:data-starting-style:-translate-x-full data-[side=right]:right-0 data-[side=right]:border-l data-[side=right]:data-ending-style:translate-x-full data-[side=right]:data-starting-style:translate-x-full",
          className,
        )}
        {...props}
      >
        {children}
        <SheetPrimitive.Close
          aria-label="Close"
          className="absolute top-3 right-3 inline-grid size-9 place-items-center rounded-md text-muted-foreground transition-colors duration-120 hover:bg-accent hover:text-foreground pointer-coarse:size-11"
        >
          <X className="size-4" aria-hidden="true" />
        </SheetPrimitive.Close>
      </SheetPrimitive.Popup>
    </SheetPrimitive.Portal>
  );
}

function SheetTitle({ className, ...props }: WithClassName<SheetPrimitive.Title.Props>) {
  return <SheetPrimitive.Title data-slot="sheet-title" className={cn("text-base font-semibold text-foreground", className)} {...props} />;
}

export { Sheet, SheetContent, SheetTitle, SheetTrigger };
```

- [ ] **Step 5: Write the alert dialog and ConfirmDialog**

Create `web/src/components/ui/alert-dialog.tsx`:

```tsx
"use client";

import { AlertDialog as AlertDialogPrimitive } from "@base-ui/react/alert-dialog";
import type { ComponentProps } from "react";

import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

function AlertDialog(props: AlertDialogPrimitive.Root.Props) {
  return <AlertDialogPrimitive.Root {...props} />;
}

function AlertDialogTrigger(props: AlertDialogPrimitive.Trigger.Props) {
  return <AlertDialogPrimitive.Trigger data-slot="alert-dialog-trigger" {...props} />;
}

function AlertDialogContent({ className, ...props }: WithClassName<AlertDialogPrimitive.Popup.Props>) {
  return (
    <AlertDialogPrimitive.Portal>
      <AlertDialogPrimitive.Backdrop className="fixed inset-0 z-50 bg-(--scrim) data-open:animate-in data-open:fade-in-0 data-open:duration-240 data-closed:animate-out data-closed:fade-out-0 data-closed:duration-160" />
      <AlertDialogPrimitive.Popup
        data-slot="alert-dialog-content"
        className={cn(
          "fixed top-1/2 left-1/2 z-50 grid w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 gap-5 rounded-xl border border-border bg-popover p-5 text-popover-foreground shadow-overlay outline-none data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-open:duration-240 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-closed:duration-160",
          className,
        )}
        {...props}
      />
    </AlertDialogPrimitive.Portal>
  );
}

function AlertDialogHeader({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="alert-dialog-header" className={cn("grid gap-2", className)} {...props} />;
}

function AlertDialogFooter({ className, ...props }: ComponentProps<"div">) {
  return (
    <div data-slot="alert-dialog-footer" className={cn("flex flex-col-reverse gap-2 sm:flex-row sm:justify-end", className)} {...props} />
  );
}

function AlertDialogTitle({ className, ...props }: WithClassName<AlertDialogPrimitive.Title.Props>) {
  return (
    <AlertDialogPrimitive.Title data-slot="alert-dialog-title" className={cn("text-base font-semibold text-foreground", className)} {...props} />
  );
}

function AlertDialogDescription({ className, ...props }: WithClassName<AlertDialogPrimitive.Description.Props>) {
  return (
    <AlertDialogPrimitive.Description
      data-slot="alert-dialog-description"
      className={cn("text-sm text-muted-foreground", className)}
      {...props}
    />
  );
}

function AlertDialogCancel({ className, ...props }: WithClassName<AlertDialogPrimitive.Close.Props>) {
  return (
    <AlertDialogPrimitive.Close
      data-slot="alert-dialog-cancel"
      type="button"
      className={cn(buttonVariants({ variant: "secondary" }), className)}
      {...props}
    />
  );
}

export {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
};
```

Create `web/src/components/confirm-dialog.tsx`:

```tsx
"use client";

import { useRef, useState, type ReactElement } from "react";

import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";

type ConfirmDialogProps = {
  trigger: ReactElement;
  title: string;
  description: string;
  confirmLabel: string;
  action: (formData: FormData) => void | Promise<void>;
};

export function ConfirmDialog({ trigger, title, description, confirmLabel, action }: ConfirmDialogProps) {
  const [open, setOpen] = useState(false);
  const cancelRef = useRef<HTMLButtonElement>(null);

  return (
    <AlertDialog open={open} onOpenChange={setOpen}>
      <AlertDialogTrigger render={trigger} />
      <AlertDialogContent initialFocus={cancelRef}>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <form action={action} onSubmit={() => setOpen(false)}>
          <AlertDialogFooter>
            <AlertDialogCancel ref={cancelRef}>Cancel</AlertDialogCancel>
            <Button type="submit" variant="destructive-solid">
              {confirmLabel}
            </Button>
          </AlertDialogFooter>
        </form>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 6: Run the new tests and watch them pass**

Run: `npm --prefix web test -- src/components/ui/overlays.test.tsx src/components/confirm-dialog.test.tsx`
Expected: PASS, 4 tests.

If the confirm test fails because the action never runs, React dropped the submission when the form unmounted. In that case, close the dialog from the action instead:
1. Remove `onSubmit`.
2. Pass `action={async (formData) => { setOpen(false); await action(formData); }}`.
3. Re-run the test.

- [ ] **Step 7: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/components/ui/dropdown-menu.tsx web/src/components/ui/sheet.tsx web/src/components/ui/alert-dialog.tsx web/src/components/ui/overlays.test.tsx web/src/components/confirm-dialog.tsx web/src/components/confirm-dialog.test.tsx
git commit -m "Add menu, sheet and confirmation dialog components on Base UI"
```

---

### Task 6: Logo, page header, link tabs, empty state and site header

**Files:**
- Create: `web/src/components/logo.tsx`, `page-header.tsx`, `link-tabs.tsx`, `empty-state.tsx`, `site-header.tsx`
- Create: `web/src/components/link-tabs.test.tsx`, `web/src/components/page-parts.test.tsx`

**Interfaces:**
- Produces:
  - **Logo:** `LogoMark({ className? })` and `Logo({ href?: string; className?: string })`. With `href`, the logo is a link named "Kairo".
  - **PageHeader:** `PageHeader({ title: string; description?: string; eyebrow?: string; actions?: ReactNode; className?: string })`, which renders the page's `h1`.
  - **LinkTabs** (client):
    - `type LinkTab = { href: string; label: string; exact?: boolean }`
    - `isCurrentPath(pathname: string | null, href: string, exact?: boolean): boolean`
    - `LinkTabs({ label: string; tabs: LinkTab[]; className?: string })`
  - **EmptyState:** `EmptyState({ icon: LucideIcon; title: string; description: string; as?: "h1" | "h2"; children?: ReactNode; className?: string })`
  - **SiteHeader:** `SiteHeader({ children?: ReactNode })`

- [ ] **Step 1: Write the failing tests**

Create `web/src/components/link-tabs.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { isCurrentPath, LinkTabs } from "@/components/link-tabs";

const navigation = vi.hoisted(() => ({ pathname: "/" }));

vi.mock("next/navigation", () => ({ usePathname: () => navigation.pathname }));

const tabs = [
  { href: "/acme/settings", label: "Workspace", exact: true },
  { href: "/acme/settings/team", label: "Team" },
];

beforeEach(() => {
  navigation.pathname = "/acme/settings/team";
});

test("marks the tab for the current page", () => {
  render(<LinkTabs label="Settings" tabs={tabs} />);

  const nav = screen.getByRole("navigation", { name: "Settings" });
  expect(within(nav).getByRole("link", { name: "Team" }).getAttribute("aria-current")).toBe("page");
  expect(within(nav).getByRole("link", { name: "Workspace" }).getAttribute("aria-current")).toBeNull();
});

test("an exact tab is current only on its own page", () => {
  navigation.pathname = "/acme/settings";
  render(<LinkTabs label="Settings" tabs={tabs} />);

  expect(screen.getByRole("link", { name: "Workspace" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("link", { name: "Team" }).getAttribute("aria-current")).toBeNull();
});

test("a tab stays current on the pages below it, and only there", () => {
  expect(isCurrentPath("/acme/campaigns/42/brief/edit", "/acme/campaigns/42/brief")).toBe(true);
  expect(isCurrentPath("/acme/campaigns-archive", "/acme/campaigns")).toBe(false);
  expect(isCurrentPath("/acme/settings/team", "/acme/settings", true)).toBe(false);
  expect(isCurrentPath(null, "/acme/today")).toBe(false);
});
```

Create `web/src/components/page-parts.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { Compass, Sun } from "lucide-react";
import { expect, test } from "vitest";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { SiteHeader } from "@/components/site-header";

test("a page header is the page's h1, with its description and actions", () => {
  render(<PageHeader title="Settings" description="Workspace details and your team." actions={<button type="button">Ask Kairo</button>} />);

  expect(screen.getByRole("heading", { level: 1, name: "Settings" })).toBeTruthy();
  expect(screen.getByText("Workspace details and your team.")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Ask Kairo" })).toBeTruthy();
});

test("an empty state explains what will appear, under a section heading", () => {
  render(<EmptyState icon={Sun} title="Nothing needs you yet" description="Approvals waiting for you will show up here." />);

  expect(screen.getByRole("heading", { level: 2, name: "Nothing needs you yet" })).toBeTruthy();
  expect(screen.getByText("Approvals waiting for you will show up here.")).toBeTruthy();
});

test("an empty state can be the page's own heading", () => {
  render(<EmptyState as="h1" icon={Compass} title="We couldn't find that page" description="It may have moved." />);

  expect(screen.getByRole("heading", { level: 1, name: "We couldn't find that page" })).toBeTruthy();
});

test("the site header links the logo home and holds its actions", () => {
  render(
    <SiteHeader>
      <button type="button">Sign out</button>
    </SiteHeader>,
  );

  expect(screen.getByRole("link", { name: "Kairo" }).getAttribute("href")).toBe("/");
  expect(screen.getByRole("button", { name: "Sign out" })).toBeTruthy();
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- src/components/link-tabs.test.tsx src/components/page-parts.test.tsx`
Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Write the components**

Create `web/src/components/logo.tsx`:

```tsx
import Link from "next/link";

import { cn } from "@/lib/utils";

export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" className={cn("size-6 shrink-0", className)}>
      <rect width="24" height="24" rx="7" className="fill-primary" />
      <path
        d="M6.5 15.5l4-4 2.5 2.5 4.5-5.5"
        fill="none"
        strokeWidth="2.3"
        strokeLinecap="round"
        strokeLinejoin="round"
        className="stroke-primary-foreground"
      />
      <circle cx="17.5" cy="8.5" r="2.1" className="fill-primary-foreground" />
    </svg>
  );
}

export function Logo({ href, className }: { href?: string; className?: string }) {
  const classes = cn("inline-flex items-center gap-2.5 rounded-md text-foreground", className);
  const content = (
    <>
      <LogoMark />
      <span className="font-display text-display-sm leading-none">Kairo</span>
    </>
  );

  return href ? (
    <Link href={href} className={classes}>
      {content}
    </Link>
  ) : (
    <span className={classes}>{content}</span>
  );
}
```

Create `web/src/components/page-header.tsx`:

```tsx
import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

type PageHeaderProps = {
  title: string;
  description?: string;
  eyebrow?: string;
  actions?: ReactNode;
  className?: string;
};

export function PageHeader({ title, description, eyebrow, actions, className }: PageHeaderProps) {
  return (
    <header className={cn("flex flex-col gap-4 md:flex-row md:items-end md:justify-between", className)}>
      <div className="min-w-0">
        {eyebrow && <p className="mb-2 text-eyebrow text-subtle-foreground uppercase">{eyebrow}</p>}
        <h1 className="font-display text-display-md text-foreground md:text-display-lg">{title}</h1>
        {description && <p className="mt-2 max-w-prose text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}
```

Create `web/src/components/link-tabs.tsx`:

```tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { cn } from "@/lib/utils";

export type LinkTab = { href: string; label: string; exact?: boolean };

export function isCurrentPath(pathname: string | null, href: string, exact = false) {
  if (!pathname) {
    return false;
  }

  return pathname === href || (!exact && pathname.startsWith(`${href}/`));
}

export function LinkTabs({ label, tabs, className }: { label: string; tabs: LinkTab[]; className?: string }) {
  const pathname = usePathname();

  return (
    <nav aria-label={label} className={cn("no-scrollbar -mx-4 overflow-x-auto px-4 md:mx-0 md:px-0", className)}>
      <ul className="flex min-w-max gap-6 border-b border-border">
        {tabs.map((tab) => (
          <li key={tab.href}>
            <Link
              href={tab.href}
              aria-current={isCurrentPath(pathname, tab.href, tab.exact) ? "page" : undefined}
              className="-mb-px inline-flex h-11 items-center border-b-2 border-transparent text-sm font-medium text-muted-foreground transition-colors duration-120 hover:text-foreground aria-[current=page]:border-primary aria-[current=page]:font-semibold aria-[current=page]:text-foreground"
            >
              {tab.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
```

Create `web/src/components/empty-state.tsx`:

```tsx
import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

type EmptyStateProps = {
  icon: LucideIcon;
  title: string;
  description: string;
  as?: "h1" | "h2";
  children?: ReactNode;
  className?: string;
};

export function EmptyState({ icon: Icon, title, description, as: Heading = "h2", children, className }: EmptyStateProps) {
  return (
    <div className={cn("flex flex-col items-center rounded-lg border border-border bg-card px-6 py-14 text-center md:py-20", className)}>
      <div className="mb-5 grid size-12 place-items-center rounded-xl bg-primary-soft text-primary">
        <Icon className="size-6" aria-hidden="true" />
      </div>
      <Heading className="font-display text-display-md text-foreground">{title}</Heading>
      <p className="mt-2 max-w-[52ch] text-muted-foreground">{description}</p>
      {children && <div className="mt-6 flex flex-wrap justify-center gap-2">{children}</div>}
    </div>
  );
}
```

Create `web/src/components/site-header.tsx`:

```tsx
import type { ReactNode } from "react";

import { Logo } from "@/components/logo";

export function SiteHeader({ children }: { children?: ReactNode }) {
  return (
    <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-sidebar px-4 md:px-6">
      <Logo href="/" />
      {children && <div className="flex items-center gap-2">{children}</div>}
    </header>
  );
}
```

- [ ] **Step 4: Run the new tests and watch them pass**

Run: `npm --prefix web test -- src/components/link-tabs.test.tsx src/components/page-parts.test.tsx`
Expected: PASS, 7 tests.

- [ ] **Step 5: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/components/logo.tsx web/src/components/page-header.tsx web/src/components/link-tabs.tsx web/src/components/link-tabs.test.tsx web/src/components/empty-state.tsx web/src/components/site-header.tsx web/src/components/page-parts.test.tsx
git commit -m "Add the logo, page header, link tabs, empty state and site header"
```

---

### Task 7: Navigation, workspace and account menus

**Files:**
- Modify: `web/src/components/nav-menu.tsx`, `nav-menu.test.tsx`
- Modify: `web/src/components/workspace-switcher.tsx`, `workspace-switcher.test.tsx`
- Modify: `web/src/components/user-menu.tsx`; create `web/src/components/user-menu.test.tsx`
- Modify: `web/src/components/sign-out-button.tsx` (its test stays as it is)
- Modify: `web/src/app/[workspace]/settings/_components/settings-nav.tsx`, `settings-nav.test.tsx`
- Modify: `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.tsx`, `campaign-room-tabs.test.tsx`, `room-tabs.test.tsx`

**Interfaces:**
- Consumes:
  - `DropdownMenu*` (Task 5)
  - `LinkTabs` and `isCurrentPath` (Task 6)
  - `UserAvatar` and `initials` (Task 4)
  - `Button` (Task 3)
- Produces:
  - `NavMenu({ workspace })`, a client component that marks the current area
  - `WorkspaceSwitcher({ current, workspaces, pendingInvitations })`, whose trigger is named "Workspace: {name}"
  - `UserMenu({ name: string; email: string; pictureUrl?: string | null })`, which opens a menu named "Account"
  - `useSignOut(): () => Promise<void>` and `SignOutButton()`
  - `SettingsNav({ workspace })` and `CampaignRoomTabs({ workspace, roomId })`, both rendering LinkTabs with a top margin

- [ ] **Step 1: Update and write the failing tests**

Replace `web/src/components/nav-menu.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { NavMenu } from "@/components/nav-menu";

const navigation = vi.hoisted(() => ({ pathname: "/acme/campaigns/42/brief" }));

vi.mock("next/navigation", () => ({ usePathname: () => navigation.pathname }));

beforeEach(() => {
  navigation.pathname = "/acme/campaigns/42/brief";
});

test("lists the seven areas in order, each linking into the workspace", () => {
  render(<NavMenu workspace="acme" />);

  const nav = screen.getByRole("navigation", { name: "Main" });
  const links = within(nav).getAllByRole("link");

  expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Today", "/acme/today"],
    ["Opportunities", "/acme/opportunities"],
    ["Campaigns", "/acme/campaigns"],
    ["Triggers", "/acme/triggers"],
    ["Impact Lab", "/acme/impact-lab"],
    ["Brand Kit", "/acme/brand-kit"],
    ["Settings", "/acme/settings"],
  ]);
});

test("marks the area the current page belongs to", () => {
  render(<NavMenu workspace="acme" />);

  expect(screen.getByRole("link", { name: "Campaigns" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("link", { name: "Today" }).getAttribute("aria-current")).toBeNull();
});
```

Replace `web/src/components/workspace-switcher.test.tsx`:

```tsx
import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { WorkspaceSwitcher } from "@/components/workspace-switcher";

const acme = { id: "1", handle: "acme", name: "Acme Shoes" };
const beta = { id: "2", handle: "beta", name: "Beta Bags" };

async function openMenu(name: string) {
  fireEvent.click(screen.getByRole("button", { name: `Workspace: ${name}` }));
  return screen.findByRole("menu", { name: "Workspaces" });
}

test("names the current workspace on its button, in full even when the text is cut short", () => {
  const long = { ...acme, name: "Acme Footwear and Monsoon Accessories Private Limited, Western India Division" };
  render(<WorkspaceSwitcher current={long} workspaces={[long, beta]} pendingInvitations={0} />);

  expect(screen.getByRole("button", { name: `Workspace: ${long.name}` }).getAttribute("title")).toBe(long.name);
});

test("links to every workspace and marks the current one", async () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme, beta]} pendingInvitations={0} />);

  const menu = await openMenu("Acme Shoes");
  const current = within(menu).getByRole("menuitem", { name: "Acme Shoes" });

  expect(current.getAttribute("href")).toBe("/acme/today");
  expect(current.getAttribute("aria-current")).toBe("page");
  expect(within(menu).getByRole("menuitem", { name: "Beta Bags" }).getAttribute("aria-current")).toBeNull();
  expect(within(menu).getByRole("menuitem", { name: "Create a workspace" }).getAttribute("href")).toBe("/welcome");
});

test("shows how many invitations are waiting", async () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme]} pendingInvitations={2} />);

  const menu = await openMenu("Acme Shoes");

  expect(within(menu).getByRole("menuitem", { name: "Invitations (2) and new workspaces" }).getAttribute("href")).toBe("/welcome");
});
```

Create `web/src/components/user-menu.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { UserMenu } from "@/components/user-menu";

const router = vi.hoisted(() => ({ replace: vi.fn(), refresh: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => router }));

afterEach(() => {
  vi.unstubAllGlobals();
});

test("shows who is signed in and signs them out from the menu", async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal("fetch", fetchMock);
  render(<UserMenu name="Neha Kapoor" email="neha@acme.example" pictureUrl={null} />);

  fireEvent.click(screen.getByRole("button", { name: "Account: Neha Kapoor" }));
  const menu = await screen.findByRole("menu", { name: "Account" });
  expect(within(menu).getByText("neha@acme.example")).toBeTruthy();

  fireEvent.click(within(menu).getByRole("menuitem", { name: "Sign out" }));

  await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/sign-in"));
  expect(fetchMock).toHaveBeenCalledWith("/api/auth/sign-out", { method: "POST", headers: { "X-Kairo-Request": "1" } });
});
```

Prepend these lines to the imports of `web/src/app/[workspace]/settings/_components/settings-nav.test.tsx`, `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.test.tsx` and `web/src/app/[workspace]/campaigns/[id]/_components/room-tabs.test.tsx`. Leave each file's assertions unchanged, and merge `vi` into the existing `vitest` import:

```tsx
import { vi } from "vitest";

vi.mock("next/navigation", () => ({ usePathname: () => null }));
```

- [ ] **Step 2: Run them and watch the new ones fail**

Run: `npm --prefix web test -- src/components "src/app/[workspace]/settings/_components" "src/app/[workspace]/campaigns"`
Expected: FAIL in `nav-menu`, `workspace-switcher` and `user-menu`. The others still pass.

- [ ] **Step 3: Rewrite the sign-out button around a hook**

Replace `web/src/components/sign-out-button.tsx`:

```tsx
"use client";

import { LogOut } from "lucide-react";
import { useRouter } from "next/navigation";

import { Button } from "@/components/ui/button";
import { REQUEST_HEADER } from "@/lib/http-names";

export function useSignOut() {
  const router = useRouter();

  return async function signOut() {
    await fetch("/api/auth/sign-out", { method: "POST", headers: { [REQUEST_HEADER]: "1" } });
    router.replace("/sign-in");
    router.refresh();
  };
}

export function SignOutButton() {
  const signOut = useSignOut();

  return (
    <Button variant="ghost" size="sm" onClick={signOut}>
      <LogOut aria-hidden="true" />
      Sign out
    </Button>
  );
}
```

- [ ] **Step 4: Rewrite the navigation menu**

Replace `web/src/components/nav-menu.tsx`:

```tsx
"use client";

import { FlaskConical, Megaphone, Palette, Settings, Sun, TrendingUp, Zap, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { isCurrentPath } from "@/components/link-tabs";

const areas: { segment: string; label: string; icon: LucideIcon }[] = [
  { segment: "today", label: "Today", icon: Sun },
  { segment: "opportunities", label: "Opportunities", icon: TrendingUp },
  { segment: "campaigns", label: "Campaigns", icon: Megaphone },
  { segment: "triggers", label: "Triggers", icon: Zap },
  { segment: "impact-lab", label: "Impact Lab", icon: FlaskConical },
  { segment: "brand-kit", label: "Brand Kit", icon: Palette },
  { segment: "settings", label: "Settings", icon: Settings },
];

export function NavMenu({ workspace }: { workspace: string }) {
  const pathname = usePathname();

  return (
    <nav aria-label="Main">
      <ul className="flex flex-col gap-0.5">
        {areas.map(({ segment, label, icon: Icon }) => {
          const href = `/${workspace}/${segment}`;

          return (
            <li key={segment}>
              <Link
                href={href}
                aria-current={isCurrentPath(pathname, href) ? "page" : undefined}
                className="group flex h-9 items-center gap-3 rounded-md px-2.5 text-sm font-medium text-muted-foreground transition-colors duration-120 hover:bg-accent hover:text-foreground aria-[current=page]:bg-primary-soft aria-[current=page]:font-semibold aria-[current=page]:text-foreground pointer-coarse:h-11"
              >
                <Icon className="size-4 text-subtle-foreground group-aria-[current=page]:text-primary" aria-hidden="true" />
                {label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
```

- [ ] **Step 5: Rewrite the workspace switcher**

Replace `web/src/components/workspace-switcher.tsx`:

```tsx
"use client";

import { Check, ChevronsUpDown, Plus } from "lucide-react";
import Link from "next/link";

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuLinkItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { initials } from "@/lib/initials";

type WorkspaceLink = { id: string; handle: string; name: string };

type WorkspaceSwitcherProps = {
  current: WorkspaceLink;
  workspaces: WorkspaceLink[];
  pendingInvitations: number;
};

export function WorkspaceSwitcher({ current, workspaces, pendingInvitations }: WorkspaceSwitcherProps) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        title={current.name}
        className="flex h-10 w-full cursor-pointer items-center gap-2.5 rounded-lg border border-border-strong bg-card px-2.5 text-left text-sm font-semibold text-foreground transition-colors duration-120 hover:bg-accent pointer-coarse:h-11"
      >
        <span aria-hidden="true" className="grid size-6 shrink-0 place-items-center rounded-md bg-primary-soft text-xs font-bold text-primary-strong">
          {initials(current.name).slice(0, 1)}
        </span>
        <span className="min-w-0 flex-1 truncate">
          <span className="sr-only">Workspace: </span>
          {current.name}
        </span>
        <ChevronsUpDown className="size-4 shrink-0 text-subtle-foreground" aria-hidden="true" />
      </DropdownMenuTrigger>
      <DropdownMenuContent aria-label="Workspaces" className="w-(--anchor-width) min-w-60">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Workspaces</DropdownMenuLabel>
          {workspaces.map((workspace) => (
            <DropdownMenuLinkItem
              key={workspace.id}
              render={<Link href={`/${workspace.handle}/today`} />}
              aria-current={workspace.id === current.id ? "page" : undefined}
              title={workspace.name}
            >
              <span className="min-w-0 flex-1 truncate">{workspace.name}</span>
              {workspace.id === current.id && <Check aria-hidden="true" />}
            </DropdownMenuLinkItem>
          ))}
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuLinkItem render={<Link href="/welcome" />}>
          <Plus aria-hidden="true" />
          {pendingInvitations > 0 ? `Invitations (${pendingInvitations}) and new workspaces` : "Create a workspace"}
        </DropdownMenuLinkItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 6: Rewrite the user menu**

Replace `web/src/components/user-menu.tsx`:

```tsx
"use client";

import { ChevronsUpDown, LogOut } from "lucide-react";

import { useSignOut } from "@/components/sign-out-button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { UserAvatar } from "@/components/user-avatar";

type UserMenuProps = { name: string; email: string; pictureUrl?: string | null };

export function UserMenu({ name, email, pictureUrl }: UserMenuProps) {
  const signOut = useSignOut();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={`Account: ${name}`}
        className="flex w-full cursor-pointer items-center gap-2.5 rounded-lg px-2 py-2 text-left transition-colors duration-120 hover:bg-accent"
      >
        <UserAvatar name={name} pictureUrl={pictureUrl} isYou />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-small font-semibold text-foreground" title={name}>
            {name}
          </span>
          <span className="block truncate text-xs text-subtle-foreground" title={email}>
            {email}
          </span>
        </span>
        <ChevronsUpDown className="size-4 shrink-0 text-subtle-foreground" aria-hidden="true" />
      </DropdownMenuTrigger>
      <DropdownMenuContent side="top" aria-label="Account" className="w-(--anchor-width) min-w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel>
            Signed in as
            <span className="block truncate text-small font-medium text-foreground">{email}</span>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={signOut}>
          <LogOut aria-hidden="true" />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 7: Move the settings and campaign-room tabs onto LinkTabs**

Replace `web/src/app/[workspace]/settings/_components/settings-nav.tsx`:

```tsx
import { LinkTabs } from "@/components/link-tabs";

export function SettingsNav({ workspace }: { workspace: string }) {
  const base = `/${workspace}/settings`;

  return (
    <LinkTabs
      label="Settings"
      className="mt-6"
      tabs={[
        { href: base, label: "Workspace", exact: true },
        { href: `${base}/team`, label: "Team" },
        { href: `${base}/approvals`, label: "Approvals" },
        { href: `${base}/audit-log`, label: "Audit log" },
      ]}
    />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.tsx`:

```tsx
import { LinkTabs } from "@/components/link-tabs";

const tabs = [
  { segment: "evidence", label: "Evidence" },
  { segment: "brief", label: "Brief" },
  { segment: "assets", label: "Assets" },
  { segment: "launch", label: "Launch" },
  { segment: "performance", label: "Performance" },
  { segment: "results", label: "Results" },
];

export function CampaignRoomTabs({ workspace, roomId }: { workspace: string; roomId: string }) {
  const base = `/${workspace}/campaigns/${encodeURIComponent(roomId)}`;

  return <LinkTabs label="Campaign room" className="mt-6" tabs={tabs.map((tab) => ({ href: `${base}/${tab.segment}`, label: tab.label }))} />;
}
```

- [ ] **Step 8: Run the tests and watch them pass**

Run: `npm --prefix web test -- src/components "src/app/[workspace]/settings/_components" "src/app/[workspace]/campaigns"`
Expected: PASS.

- [ ] **Step 9: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/components "web/src/app/[workspace]/settings/_components" "web/src/app/[workspace]/campaigns/[id]/_components"
git commit -m "Rebuild the navigation: area menu, workspace and account menus, and link tabs"
```

---

### Task 8: The app shell, phone drawer and skip link

**Files:**
- Create: `web/src/components/app-shell.tsx`, `mobile-nav.tsx`, `skeletons.tsx`
- Create: `web/src/components/app-shell.test.tsx`, `web/src/components/mobile-nav.test.tsx`
- Create: `web/src/app/[workspace]/_components/workspace-name.tsx`
- Modify: `web/src/app/[workspace]/layout.tsx`, `web/src/app/[workspace]/_components/workspace-sidebar.tsx` (replace each)

**Interfaces:**
- Consumes: `Sheet*` (Task 5), `Logo` (Task 6), `NavMenu`, `WorkspaceSwitcher` and `UserMenu` (Task 7), `Skeleton` (Task 4).
- Produces:
  - `AppShell({ sidebar: ReactNode; topBar?: ReactNode; children: ReactNode })`
  - `MobileNav({ context?: ReactNode; children: ReactNode })`, which opens a sheet named "Navigation" from a button named "Open navigation"
  - `SidebarSkeleton()` and `PageSkeleton()`

- [ ] **Step 1: Write the failing tests**

Create `web/src/components/mobile-nav.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { MobileNav } from "@/components/mobile-nav";

function renderNav() {
  render(
    <MobileNav context={<span>Acme Shoes</span>}>
      <nav aria-label="Main">
        <a href="#today">Today</a>
      </nav>
    </MobileNav>,
  );
  return screen.getByRole("button", { name: "Open navigation" });
}

test("shows the workspace on the top bar and opens the navigation in a drawer", async () => {
  const button = renderNav();

  expect(screen.getByText("Acme Shoes")).toBeTruthy();
  fireEvent.click(button);

  const drawer = await screen.findByRole("dialog", { name: "Navigation" });
  expect(within(drawer).getByRole("navigation", { name: "Main" })).toBeTruthy();
});

test("closes once a page is chosen", async () => {
  fireEvent.click(renderNav());
  const drawer = await screen.findByRole("dialog", { name: "Navigation" });

  fireEvent.click(within(drawer).getByRole("link", { name: "Today" }));

  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
});

test("closes on Escape and gives focus back to the menu button", async () => {
  const button = renderNav();
  fireEvent.click(button);
  await screen.findByRole("dialog", { name: "Navigation" });

  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });

  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(button));
});
```

Create `web/src/components/app-shell.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { AppShell } from "@/components/app-shell";

test("lets keyboard users skip straight to the page", () => {
  render(
    <AppShell sidebar={<p>Sidebar</p>}>
      <p>Page</p>
    </AppShell>,
  );

  expect(screen.getByRole("link", { name: "Skip to content" }).getAttribute("href")).toBe("#main");
  expect(screen.getByRole("main").id).toBe("main");
  expect(screen.getByRole("main").textContent).toBe("Page");
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- src/components/mobile-nav.test.tsx src/components/app-shell.test.tsx`
Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Write the shell pieces**

Create `web/src/components/mobile-nav.tsx`:

```tsx
"use client";

import { Menu } from "lucide-react";
import { useState, type MouseEvent, type ReactNode } from "react";

import { Logo } from "@/components/logo";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";

export function MobileNav({ context, children }: { context?: ReactNode; children: ReactNode }) {
  const [open, setOpen] = useState(false);

  function closeOnLink(event: MouseEvent<HTMLDivElement>) {
    if ((event.target as Element).closest("a")) {
      setOpen(false);
    }
  }

  return (
    <div className="sticky top-0 z-40 flex h-14 items-center gap-2 border-b border-border bg-sidebar px-2 lg:hidden">
      <Sheet open={open} onOpenChange={setOpen}>
        <SheetTrigger
          aria-label="Open navigation"
          className="inline-grid size-11 shrink-0 cursor-pointer place-items-center rounded-md text-foreground transition-colors duration-120 hover:bg-accent"
        >
          <Menu className="size-5" aria-hidden="true" />
        </SheetTrigger>
        <SheetContent side="left" className="bg-sidebar">
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <div className="h-full overflow-y-auto" onClick={closeOnLink}>
            {children}
          </div>
        </SheetContent>
      </Sheet>
      <Logo href="/" />
      {context && <div className="ml-auto min-w-0 truncate pr-2 text-small font-semibold text-muted-foreground">{context}</div>}
    </div>
  );
}
```

Create `web/src/components/app-shell.tsx`:

```tsx
import type { ReactNode } from "react";

import { MobileNav } from "@/components/mobile-nav";

type AppShellProps = {
  sidebar: ReactNode;
  topBar?: ReactNode;
  children: ReactNode;
};

export function AppShell({ sidebar, topBar, children }: AppShellProps) {
  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[15.5rem_minmax(0,1fr)]">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-card px-4 py-2 text-sm font-semibold text-foreground shadow-popover focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
      >
        Skip to content
      </a>
      <aside className="sticky top-0 hidden h-dvh overflow-y-auto border-r border-border bg-sidebar lg:block">{sidebar}</aside>
      <MobileNav context={topBar}>{sidebar}</MobileNav>
      <main id="main" tabIndex={-1} className="min-w-0 px-4 py-6 focus-visible:outline-none md:px-6 md:py-8 lg:px-10 lg:py-10">
        <div className="mx-auto w-full max-w-300">{children}</div>
      </main>
    </div>
  );
}
```

Create `web/src/components/skeletons.tsx`:

```tsx
import { Skeleton } from "@/components/ui/skeleton";

export function SidebarSkeleton() {
  return (
    <div className="flex h-full flex-col gap-5 px-3 pt-4 pb-3" aria-hidden="true">
      <Skeleton className="h-7 w-24" />
      <Skeleton className="h-10 w-full rounded-lg" />
      <div className="flex flex-col gap-2">
        {Array.from({ length: 7 }, (_, index) => (
          <Skeleton key={index} className="h-8 w-full" />
        ))}
      </div>
    </div>
  );
}

export function PageSkeleton() {
  return (
    <div className="flex flex-col gap-8">
      <span role="status" className="sr-only">
        Loading
      </span>
      <div className="flex flex-col gap-3" aria-hidden="true">
        <Skeleton className="h-10 w-56" />
        <Skeleton className="h-5 w-full max-w-md" />
      </div>
      <Skeleton className="h-64 w-full rounded-lg" aria-hidden="true" />
    </div>
  );
}
```

- [ ] **Step 4: Run the new tests and watch them pass**

Run: `npm --prefix web test -- src/components/mobile-nav.test.tsx src/components/app-shell.test.tsx`
Expected: PASS, 4 tests.

- [ ] **Step 5: Put the workspace screens in the shell**

Create `web/src/app/[workspace]/_components/workspace-name.tsx`:

```tsx
import { getWorkspace } from "@/lib/core-api/queries";

export async function WorkspaceName({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const { name } = await getWorkspace(workspace);

  return <span title={name}>{name}</span>;
}
```

Replace `web/src/app/[workspace]/_components/workspace-sidebar.tsx`:

```tsx
import { notFound } from "next/navigation";

import { Logo } from "@/components/logo";
import { NavMenu } from "@/components/nav-menu";
import { UserMenu } from "@/components/user-menu";
import { WorkspaceSwitcher } from "@/components/workspace-switcher";
import { getMe, getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";

export async function WorkspaceSidebar({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const [workspaces, me, invitations] = await Promise.all([getMyWorkspaces(), getMe(), getMyInvitations()]);
  const current = workspaces.find((candidate) => candidate.handle === workspace);
  if (!current) {
    notFound();
  }

  return (
    <div className="flex min-h-full flex-col gap-5 px-3 pt-4 pb-3">
      <Logo href={`/${current.handle}/today`} className="self-start px-2 py-1" />
      <WorkspaceSwitcher current={current} workspaces={workspaces} pendingInvitations={invitations.length} />
      <NavMenu workspace={current.handle} />
      <div className="mt-auto border-t border-border pt-3">
        <UserMenu name={me.name} email={me.email} pictureUrl={me.pictureUrl} />
      </div>
    </div>
  );
}
```

Replace `web/src/app/[workspace]/layout.tsx`:

```tsx
import { Suspense } from "react";

import { AppShell } from "@/components/app-shell";
import { PageSkeleton, SidebarSkeleton } from "@/components/skeletons";

import { WorkspaceName } from "./_components/workspace-name";
import { WorkspaceSidebar } from "./_components/workspace-sidebar";

export default function WorkspaceLayout({ children, params }: LayoutProps<"/[workspace]">) {
  return (
    <AppShell
      sidebar={
        <Suspense fallback={<SidebarSkeleton />}>
          <WorkspaceSidebar params={params} />
        </Suspense>
      }
      topBar={
        <Suspense fallback={null}>
          <WorkspaceName params={params} />
        </Suspense>
      }
    >
      <Suspense fallback={<PageSkeleton />}>{children}</Suspense>
    </AppShell>
  );
}
```

- [ ] **Step 6: Run the suite and the build**

Run: `npm --prefix web test`
Expected: PASS.

Run: `npm --prefix web run build`
Expected: the build succeeds.

- [ ] **Step 7: Commit**

```bash
git add web/src/components/app-shell.tsx web/src/components/app-shell.test.tsx web/src/components/mobile-nav.tsx web/src/components/mobile-nav.test.tsx web/src/components/skeletons.tsx "web/src/app/[workspace]/layout.tsx" "web/src/app/[workspace]/_components"
git commit -m "Put the workspace screens in the new shell, with a phone drawer and a skip link"
```

---

### Task 9: Sign-in

**Files:**
- Modify: `web/src/app/sign-in/page.tsx`, `_components/sign-in-panel.tsx`, `_components/sign-in-panel.test.tsx`
- Create: `web/src/app/sign-in/_components/google-mark.tsx`, `_components/signal-motif.tsx`

**Interfaces:**
- Consumes: `Alert` (Task 4), `buttonVariants` (Task 3), `Logo` (Task 6), `Skeleton` (Task 4).

- [ ] **Step 1: Update the test for the new label**

In `web/src/app/sign-in/_components/sign-in-panel.test.tsx`, change `{ name: "Sign in with Google" }` to `{ name: "Continue with Google" }`. Leave everything else as it is.

- [ ] **Step 2: Run it and watch it fail**

Run: `npm --prefix web test -- src/app/sign-in`
Expected: FAIL. The link is still named "Sign in with Google".

- [ ] **Step 3: Write the panel and its marks**

Create `web/src/app/sign-in/_components/google-mark.tsx`:

```tsx
export function GoogleMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 48 48" aria-hidden="true" className={className}>
      <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z" />
      <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z" />
      <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z" />
      <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z" />
    </svg>
  );
}
```

Create `web/src/app/sign-in/_components/signal-motif.tsx`:

```tsx
export function SignalMotif({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 600 260" aria-hidden="true" className={className}>
      <path d="M0 214 C 120 210, 220 204, 300 190 S 470 128, 588 52 L 588 112 C 470 172, 380 204, 300 210 S 120 224, 0 228 Z" className="fill-primary/10" />
      <path d="M0 220 C 140 217, 260 215, 360 213 S 520 208, 588 205" fill="none" strokeWidth="2" strokeDasharray="6 8" strokeLinecap="round" className="stroke-subtle-foreground/60" />
      <path d="M0 220 C 120 216, 220 210, 300 197 S 470 140, 588 74" fill="none" strokeWidth="3" strokeLinecap="round" className="stroke-primary" />
      <circle cx="588" cy="74" r="7" className="fill-primary" />
    </svg>
  );
}
```

Replace `web/src/app/sign-in/_components/sign-in-panel.tsx`:

```tsx
import { Alert } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

import { GoogleMark } from "./google-mark";

const errors: Record<string, string> = {
  unverified_email: "Google hasn't verified the email on that account, so it can't be used to sign in.",
  sign_in_failed: "Sign-in didn't finish. Please try again.",
};

type SignInPanelProps = {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

export async function SignInPanel({ searchParams }: SignInPanelProps) {
  const { returnUrl, error } = await searchParams;
  const target = typeof returnUrl === "string" ? returnUrl : "/";
  const message = typeof error === "string" ? (errors[error] ?? errors.sign_in_failed) : null;

  return (
    <div className="flex flex-col gap-4">
      {message && <Alert variant="destructive">{message}</Alert>}
      <a
        href={`/api/auth/sign-in?returnUrl=${encodeURIComponent(target)}`}
        className={cn(buttonVariants({ variant: "secondary", size: "lg" }), "w-full gap-3")}
      >
        <GoogleMark className="size-[1.125rem]" />
        Continue with Google
      </a>
    </div>
  );
}
```

- [ ] **Step 4: Write the split-screen page**

Replace `web/src/app/sign-in/page.tsx`:

```tsx
import type { Metadata } from "next";
import { Suspense } from "react";

import { Logo } from "@/components/logo";
import { Skeleton } from "@/components/ui/skeleton";

import { SignalMotif } from "./_components/signal-motif";
import { SignInPanel } from "./_components/sign-in-panel";

export const metadata: Metadata = { title: "Sign in" };

export default function SignInPage({ searchParams }: PageProps<"/sign-in">) {
  return (
    <div className="grid min-h-dvh bg-background lg:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]">
      <div className="relative hidden flex-col overflow-hidden border-r border-border bg-sidebar p-10 lg:flex xl:p-14">
        <Logo />
        <div className="mt-auto max-w-xl">
          <p className="font-display text-display-xl text-foreground">From rising demand to proven lift.</p>
          <p className="mt-4 max-w-prose text-base text-muted-foreground">
            Kairo finds rising demand, drafts on-brand Google Ads campaigns and measures the extra sales they cause. People approve every
            step.
          </p>
        </div>
        <SignalMotif className="mt-12 w-full max-w-xl" />
      </div>
      <main className="flex flex-col px-4 py-8 md:px-8">
        <Logo className="lg:hidden" />
        <div className="mx-auto my-auto w-full max-w-sm py-12">
          <h1 className="font-display text-display-md text-foreground md:text-display-lg">Sign in</h1>
          <p className="mt-2 text-muted-foreground">Use your Google account. Kairo reads only your name, email address and profile picture.</p>
          <div className="mt-8">
            <Suspense fallback={<Skeleton className="h-10 w-full" />}>
              <SignInPanel searchParams={searchParams} />
            </Suspense>
          </div>
        </div>
      </main>
    </div>
  );
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `npm --prefix web test -- src/app/sign-in`
Expected: PASS, 3 tests.

- [ ] **Step 6: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/app/sign-in
git commit -m "Redesign sign-in as a split screen with Continue with Google"
```

---

### Task 10: Welcome, workspace form and approvals form

**Files:**
- Modify: `web/src/app/welcome/page.tsx`, `_components/invitation-list.tsx`, `_components/create-workspace-form.tsx`
- Create: `web/src/app/welcome/_components/welcome-header.tsx`, `welcome-header.test.tsx`
- Modify: `web/src/app/[workspace]/settings/layout.tsx`, `settings/page.tsx`, `_components/workspace-form.tsx`, `_components/workspace-form.test.tsx`
- Modify: `web/src/app/[workspace]/settings/approvals/page.tsx`, `_components/approvals-form.tsx`, `_components/approvals-form.test.tsx`

**Interfaces:**
- Consumes:
  - from Task 3: `Field`, `FieldDescription`, `Label`, `Input`, `NativeSelect`, `Checkbox`, `Button`, `FieldError`, `FormStatus`
  - from Task 4: `Card*`, `Alert`, `Skeleton`, `initials`
  - from Task 6: `PageHeader`, `SiteHeader`
  - from Task 7: `SignOutButton`, `SettingsNav`
- Produces: `WelcomeHeader()`.

- [ ] **Step 1: Write the failing tests**

Create `web/src/app/welcome/_components/welcome-header.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { WelcomeHeader } from "./welcome-header";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn(), refresh: vi.fn() }) }));

test("lets someone without a workspace sign out", () => {
  render(<WelcomeHeader />);

  expect(screen.getByRole("link", { name: "Kairo" }).getAttribute("href")).toBe("/");
  expect(screen.getByRole("button", { name: "Sign out" })).toBeTruthy();
});
```

Append to `web/src/app/[workspace]/settings/_components/workspace-form.test.tsx`:

```tsx
test("tells members who aren't admins why they can't edit", () => {
  render(<WorkspaceForm action={vi.fn()} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit={false} />);

  expect(screen.getByText("Only admins can change these details.")).toBeTruthy();
});
```

Append to `web/src/app/[workspace]/settings/approvals/_components/approvals-form.test.tsx`:

```tsx
test("tells members who aren't admins why they can't edit", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit={false} />);

  expect(screen.getByText("Only admins can change who approves what.")).toBeTruthy();
});

test("explains an empty list of approvers", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={[]} canEdit />);

  expect(screen.getAllByText("Nobody has the approver role yet. Give someone the role on the Team page.")).toHaveLength(4);
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- src/app/welcome "src/app/[workspace]/settings"`
Expected: FAIL in the three new tests and `welcome-header`.

- [ ] **Step 3: Rebuild the welcome page**

Create `web/src/app/welcome/_components/welcome-header.tsx`:

```tsx
import { SignOutButton } from "@/components/sign-out-button";
import { SiteHeader } from "@/components/site-header";

export function WelcomeHeader() {
  return (
    <SiteHeader>
      <SignOutButton />
    </SiteHeader>
  );
}
```

Replace `web/src/app/welcome/_components/invitation-list.tsx`:

```tsx
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { roleLabels, type MyInvitation } from "@/lib/core-api/types";

type InvitationListProps = {
  invitations: MyInvitation[];
  accept: (invitationId: string) => Promise<void>;
  decline: (invitationId: string) => Promise<void>;
};

export function InvitationList({ invitations, accept, decline }: InvitationListProps) {
  return (
    <Card className="gap-0 overflow-hidden py-0 md:gap-0 md:py-0">
      <ul className="divide-y divide-border">
        {invitations.map((invitation) => (
          <li key={invitation.id} className="flex flex-col gap-3 px-4 py-4 md:flex-row md:items-center md:justify-between md:px-5">
            <div className="min-w-0">
              <p className="truncate font-semibold text-foreground" title={invitation.workspaceName}>
                {invitation.workspaceName}
              </p>
              <p className="text-small text-subtle-foreground">
                {invitation.invitedBy} invited you as {invitation.roles.map((role) => roleLabels[role]).join(", ")}.
              </p>
            </div>
            <div className="flex gap-2">
              <form action={accept.bind(null, invitation.id)} className="flex-1 md:flex-none">
                <Button type="submit" variant="secondary" size="sm" className="w-full">
                  Accept
                </Button>
              </form>
              <form action={decline.bind(null, invitation.id)} className="flex-1 md:flex-none">
                <Button type="submit" variant="ghost" size="sm" className="w-full">
                  Decline
                </Button>
              </form>
            </div>
          </li>
        ))}
      </ul>
    </Card>
  );
}
```

Replace `web/src/app/welcome/_components/create-workspace-form.tsx`:

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { Field, FieldDescription } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { idle, type FormState } from "@/lib/forms";
import { DEFAULT_TIME_ZONE } from "@/lib/time-zones";

type CreateWorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  timeZones: string[];
  currencies: string[];
};

export function CreateWorkspaceForm({ action, timeZones, currencies }: CreateWorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <Field>
        <Label htmlFor="name">Brand name</Label>
        <Input id="name" name="name" required maxLength={100} autoComplete="organization" aria-describedby="name-error" aria-invalid={errors.name ? true : undefined} />
        <FieldError id="name-error" errors={errors.name} />
      </Field>
      <Field>
        <Label htmlFor="handle">Handle</Label>
        <FieldDescription id="handle-help">
          3 to 40 lowercase letters, digits and single hyphens, starting with a letter. It appears in your workspace&apos;s web address, like /acme.
        </FieldDescription>
        <Input
          id="handle"
          name="handle"
          required
          minLength={3}
          maxLength={40}
          autoCapitalize="none"
          spellCheck={false}
          aria-describedby="handle-help handle-error"
          aria-invalid={errors.handle ? true : undefined}
        />
        <FieldError id="handle-error" errors={errors.handle} />
      </Field>
      <div className="grid gap-5 md:grid-cols-2">
        <Field>
          <Label htmlFor="timeZone">Time zone</Label>
          <NativeSelect id="timeZone" name="timeZone" defaultValue={DEFAULT_TIME_ZONE} aria-describedby="timeZone-error">
            {timeZones.map((zone) => (
              <option key={zone} value={zone}>
                {zone}
              </option>
            ))}
          </NativeSelect>
          <FieldError id="timeZone-error" errors={errors.timeZone} />
        </Field>
        <Field>
          <Label htmlFor="currency">Currency</Label>
          <NativeSelect id="currency" name="currency" defaultValue="INR" aria-describedby="currency-error">
            {currencies.map((currency) => (
              <option key={currency} value={currency}>
                {currency}
              </option>
            ))}
          </NativeSelect>
          <FieldError id="currency-error" errors={errors.currency} />
        </Field>
      </div>
      <FormStatus state={state} />
      <Button type="submit" pending={pending} className="self-start max-md:w-full">
        Create workspace
      </Button>
    </form>
  );
}
```

Replace `web/src/app/welcome/page.tsx`:

```tsx
import { ArrowRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { PageHeader } from "@/components/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";
import { initials } from "@/lib/initials";
import { timeZoneOptions } from "@/lib/time-zones";

import { acceptInvitation, createWorkspace, declineInvitation } from "./actions";
import { CreateWorkspaceForm } from "./_components/create-workspace-form";
import { InvitationList } from "./_components/invitation-list";
import { WelcomeHeader } from "./_components/welcome-header";

const currencies = ["INR", "USD", "EUR", "GBP", "AED", "SGD", "AUD", "CAD", "JPY"];

export const metadata: Metadata = { title: "Welcome" };

export default function WelcomePage() {
  return (
    <div className="min-h-dvh bg-background">
      <WelcomeHeader />
      <main className="mx-auto flex w-full max-w-2xl flex-col gap-10 px-4 py-10 md:py-14">
        <PageHeader title="Welcome to Kairo" description="Join a workspace you've been invited to, or create one for your brand." />
        <Suspense fallback={<Skeleton className="h-24 w-full rounded-lg" />}>
          <YourWorkspaces />
        </Suspense>
        <Card>
          <CardHeader>
            <CardTitle>Create a workspace</CardTitle>
            <CardDescription>One workspace holds one brand: its team, approvals and campaigns.</CardDescription>
          </CardHeader>
          <CardContent>
            <CreateWorkspaceForm action={createWorkspace} timeZones={timeZoneOptions()} currencies={currencies} />
          </CardContent>
        </Card>
      </main>
    </div>
  );
}

async function YourWorkspaces() {
  const [invitations, workspaces] = await Promise.all([getMyInvitations(), getMyWorkspaces()]);

  return (
    <>
      {invitations.length > 0 && (
        <section className="flex flex-col gap-3">
          <h2 className="text-base font-semibold text-foreground">Invitations</h2>
          <InvitationList invitations={invitations} accept={acceptInvitation} decline={declineInvitation} />
        </section>
      )}
      {workspaces.length > 0 && (
        <section className="flex flex-col gap-3">
          <h2 className="text-base font-semibold text-foreground">Your workspaces</h2>
          <Card className="gap-0 overflow-hidden py-0 md:gap-0 md:py-0">
            <ul className="divide-y divide-border">
              {workspaces.map((workspace) => (
                <li key={workspace.id}>
                  <Link
                    href={`/${workspace.handle}/today`}
                    className="flex items-center gap-3 px-4 py-3.5 transition-colors duration-120 hover:bg-accent md:px-5"
                  >
                    <span aria-hidden="true" className="grid size-8 shrink-0 place-items-center rounded-md bg-primary-soft text-small font-bold text-primary-strong">
                      {initials(workspace.name).slice(0, 1)}
                    </span>
                    <span className="min-w-0 flex-1 truncate font-semibold text-foreground" title={workspace.name}>
                      {workspace.name}
                    </span>
                    <ArrowRight className="size-4 text-subtle-foreground" aria-hidden="true" />
                  </Link>
                </li>
              ))}
            </ul>
          </Card>
        </section>
      )}
    </>
  );
}
```

- [ ] **Step 4: Rebuild the settings layout and workspace form**

Replace `web/src/app/[workspace]/settings/layout.tsx`:

```tsx
import { Suspense } from "react";

import { PageHeader } from "@/components/page-header";

import { SettingsNav } from "./_components/settings-nav";

export default function SettingsLayout({ children, params }: LayoutProps<"/[workspace]/settings">) {
  return (
    <div className="flex flex-col gap-8">
      <div>
        <PageHeader title="Settings" description="Workspace details, your team, who approves what, and the audit log." />
        <Suspense fallback={<div className="mt-6 h-11 border-b border-border" />}>
          <SettingsNavFor params={params} />
        </Suspense>
      </div>
      {children}
    </div>
  );
}

async function SettingsNavFor({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  return <SettingsNav workspace={workspace} />;
}
```

In `web/src/app/[workspace]/settings/page.tsx`, add `import type { Metadata } from "next";` to the imports and this line before the default export:

```tsx
export const metadata: Metadata = { title: "Workspace settings" };
```

Replace `web/src/app/[workspace]/settings/_components/workspace-form.tsx`:

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import type { WorkspaceDetails } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type WorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  workspace: WorkspaceDetails;
  timeZones: string[];
  canEdit: boolean;
};

export function WorkspaceForm({ action, workspace, timeZones, canEdit }: WorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const zones = timeZones.includes(workspace.timeZone) ? timeZones : [workspace.timeZone, ...timeZones];
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="max-w-160">
      <Card>
        <CardHeader>
          <CardTitle>Workspace details</CardTitle>
          <CardDescription>{canEdit ? "The name and time zone everyone in this workspace sees." : "Only admins can change these details."}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <fieldset disabled={!canEdit || pending} className="flex min-w-0 flex-col gap-5">
            <Field>
              <Label htmlFor="name">Brand name</Label>
              <Input
                id="name"
                name="name"
                defaultValue={workspace.name}
                required
                maxLength={100}
                aria-describedby="name-error"
                aria-invalid={errors.name ? true : undefined}
              />
              <FieldError id="name-error" errors={errors.name} />
            </Field>
            <Field>
              <Label htmlFor="timeZone">Time zone</Label>
              <NativeSelect id="timeZone" name="timeZone" defaultValue={workspace.timeZone} aria-describedby="timeZone-error">
                {zones.map((zone) => (
                  <option key={zone} value={zone}>
                    {zone}
                  </option>
                ))}
              </NativeSelect>
              <FieldError id="timeZone-error" errors={errors.timeZone} />
            </Field>
          </fieldset>
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-8 gap-y-2 text-sm">
            <dt className="text-subtle-foreground">Handle</dt>
            <dd className="font-mono text-small text-foreground">{workspace.handle}</dd>
            <dt className="text-subtle-foreground">Currency</dt>
            <dd className="text-foreground">{workspace.currency}</dd>
          </dl>
        </CardContent>
        {canEdit && (
          <CardFooter>
            <Button type="submit" pending={pending}>
              Save changes
            </Button>
            <FormStatus state={state} />
          </CardFooter>
        )}
      </Card>
    </form>
  );
}
```

- [ ] **Step 5: Rebuild the approvals form**

In `web/src/app/[workspace]/settings/approvals/page.tsx`, add `import type { Metadata } from "next";` and this line before the default export:

```tsx
export const metadata: Metadata = { title: "Approvals" };
```

Replace `web/src/app/[workspace]/settings/approvals/_components/approvals-form.tsx`:

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { ApprovalSettings } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type Duty = "briefApprovers" | "assetApprovers" | "launchApprovers" | "budgetOwners";

const duties: { field: Duty; label: string; help: string }[] = [
  { field: "briefApprovers", label: "Brief approvers", help: "Approve briefs, gate 1." },
  { field: "assetApprovers", label: "Asset approvers", help: "Approve the asset set, gate 2." },
  { field: "launchApprovers", label: "Launch approvers", help: "Approve launches, gate 3." },
  { field: "budgetOwners", label: "Budget owners", help: "Approve briefs and launches above the budget limit." },
];

type ApprovalsFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  settings: ApprovalSettings;
  approvers: { userId: string; name: string }[];
  canEdit: boolean;
};

export function ApprovalsForm({ action, settings, approvers, canEdit }: ApprovalsFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-6">
      {!canEdit && <Alert>Only admins can change who approves what.</Alert>}
      <fieldset disabled={!canEdit || pending} className="flex min-w-0 flex-col gap-6">
        <div className="grid gap-4 md:grid-cols-2">
          {duties.map((duty) => (
            <Card key={duty.field}>
              <fieldset className="min-w-0 px-4 md:px-5">
                <legend className="text-base font-semibold text-foreground">{duty.label}</legend>
                <p className="mt-1 text-small text-subtle-foreground">{duty.help}</p>
                {approvers.length === 0 ? (
                  <p className="mt-4 text-small text-muted-foreground">Nobody has the approver role yet. Give someone the role on the Team page.</p>
                ) : (
                  <ul className="mt-4 flex flex-col gap-2.5">
                    {approvers.map((approver) => (
                      <li key={approver.userId}>
                        <label className="flex cursor-pointer items-center gap-2.5 text-sm text-foreground">
                          <Checkbox name={duty.field} value={approver.userId} defaultChecked={settings[duty.field].includes(approver.userId)} />
                          <span className="min-w-0 truncate" title={approver.name}>
                            {approver.name}
                          </span>
                        </label>
                      </li>
                    ))}
                  </ul>
                )}
                <div className="mt-2">
                  <FieldError id={`${duty.field}-error`} errors={errors[duty.field]} />
                </div>
              </fieldset>
            </Card>
          ))}
        </div>
        <Card>
          <CardContent className="flex flex-col gap-1.5">
            <Label htmlFor="budgetLimit" className="text-base">
              Per-campaign budget limit ({settings.currency})
            </Label>
            <p id="budgetLimit-help" className="text-small text-subtle-foreground">
              Leave it empty and every brief and launch goes to a budget owner.
            </p>
            <div className="relative mt-1.5 max-w-xs">
              <span aria-hidden="true" className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-small font-medium text-subtle-foreground">
                {settings.currency}
              </span>
              <Input
                id="budgetLimit"
                name="budgetLimit"
                type="number"
                min="0"
                step="0.01"
                inputMode="decimal"
                defaultValue={settings.budgetLimit ?? ""}
                aria-describedby="budgetLimit-help budgetLimit-error"
                aria-invalid={errors.budgetLimit ? true : undefined}
                className="pl-14 tabular-nums"
              />
            </div>
            <FieldError id="budgetLimit-error" errors={errors.budgetLimit} />
          </CardContent>
        </Card>
      </fieldset>
      {canEdit && (
        <div className="flex flex-col gap-3">
          <Button type="submit" pending={pending} className="self-start max-md:w-full">
            Save approval settings
          </Button>
          <FormStatus state={state} />
        </div>
      )}
    </form>
  );
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `npm --prefix web test -- src/app/welcome "src/app/[workspace]/settings"`
Expected: PASS. The existing assertions on "Brand name", "Time zone", "INR", "Asset approvers", "Per-campaign budget limit (INR)" and the exact create-workspace `FormData` still hold.

- [ ] **Step 7: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add web/src/app/welcome "web/src/app/[workspace]/settings/layout.tsx" "web/src/app/[workspace]/settings/page.tsx" "web/src/app/[workspace]/settings/_components" "web/src/app/[workspace]/settings/approvals"
git commit -m "Redesign the welcome page, workspace details and approval settings"
```

---

### Task 11: Team settings and the audit log

**Files:**
- Modify: `web/src/app/[workspace]/settings/team/page.tsx`, `_components/member-row.tsx`, `_components/member-row.test.tsx`, `_components/invite-form.tsx`, `_components/pending-invitations.tsx`, `_components/pending-invitations.test.tsx`
- Modify: `web/src/app/[workspace]/settings/audit-log/page.tsx`, `_components/audit-log-table.tsx`

**Interfaces:**
- Consumes:
  - from Task 5: `ConfirmDialog`
  - from Task 3: `RoleCheckboxes`, `Button`, `buttonVariants`
  - from Task 4: `Badge`, `UserAvatar`, `Card*`, `Table*`, `Alert`
  - from Task 6: `EmptyState`

- [ ] **Step 1: Update the tests for confirmation dialogs and long names**

Replace `web/src/app/[workspace]/settings/team/_components/member-row.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { MemberRow } from "./member-row";

const priya = {
  userId: "u2",
  name: "Priya Shah",
  email: "priya@example.com",
  pictureUrl: null,
  roles: ["marketer" as const],
  joinedAt: "2026-10-13T00:00:00Z",
};

test("lets an admin change a member's roles", async () => {
  const changeRoles = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.getAll("roles")).toEqual(["marketer", "approver"]);
    return { status: "saved", message: "Roles saved." };
  });
  render(<MemberRow member={priya} isSelf={false} canManage changeRoles={changeRoles} remove={vi.fn()} />);

  fireEvent.click(screen.getByLabelText("Approver"));
  fireEvent.click(screen.getByRole("button", { name: "Save roles" }));

  expect((await screen.findByRole("status")).textContent).toBe("Roles saved.");
});

test("shows the reason when leaving is refused", async () => {
  const remove = vi.fn(async (): Promise<FormState> => ({ status: "error", message: "A workspace needs at least one admin." }));
  render(<MemberRow member={priya} isSelf canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Leave workspace" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Leave this workspace?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Leave workspace" }));

  expect((await screen.findByRole("alert")).textContent).toBe("A workspace needs at least one admin.");
});

test("asks before removing someone and does nothing if cancelled", async () => {
  const remove = vi.fn();
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(remove).not.toHaveBeenCalled();
});

test("removes someone once confirmed", async () => {
  const remove = vi.fn(async (): Promise<FormState> => ({ status: "saved", message: "Removed." }));
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Remove Priya Shah?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Remove" }));

  await waitFor(() => expect(remove).toHaveBeenCalled());
});

test("shows roles as text to members who are not admins", () => {
  render(<MemberRow member={priya} isSelf={false} canManage={false} remove={vi.fn()} changeRoles={vi.fn()} />);

  expect(screen.getByText("Marketer")).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
});

test("keeps long names and emails whole in a title", () => {
  const long = { ...priya, name: "Priyadarshini Venkataraghavan Subramanian-Iyer", email: "priyadarshini.venkataraghavan@acme-footwear.example" };
  render(<MemberRow member={long} isSelf={false} canManage={false} remove={vi.fn()} changeRoles={vi.fn()} />);

  expect(screen.getByText(long.name).getAttribute("title")).toBe(long.name);
  expect(screen.getByText(long.email).getAttribute("title")).toBe(long.email);
});
```

Replace `web/src/app/[workspace]/settings/team/_components/pending-invitations.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { PendingInvitations } from "./pending-invitations";

const invitation = {
  id: "inv-1",
  email: "priya@example.com",
  roles: ["marketer" as const],
  invitedBy: "Neha Rao",
  sentAt: "2026-10-13T00:00:00Z",
  expiresAt: "2026-10-27T00:00:00Z",
};

test("revokes the chosen invitation once confirmed", async () => {
  const revoke = vi.fn(async () => {});
  render(<PendingInvitations invitations={[invitation]} revoke={revoke} />);

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
  const dialog = await screen.findByRole("alertdialog", { name: "Revoke the invitation to priya@example.com?" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Revoke" }));

  await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
});

test("keeps the invitation when cancelled", async () => {
  const revoke = vi.fn(async () => {});
  render(<PendingInvitations invitations={[invitation]} revoke={revoke} />);

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
  const dialog = await screen.findByRole("alertdialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

  await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
  expect(revoke).not.toHaveBeenCalled();
});

test("says so when nobody is waiting", () => {
  render(<PendingInvitations invitations={[]} revoke={vi.fn()} />);

  expect(screen.getByText("No invitations are waiting.")).toBeTruthy();
});
```

- [ ] **Step 2: Run them and watch them fail**

Run: `npm --prefix web test -- "src/app/[workspace]/settings/team"`
Expected: FAIL. No dialogs exist yet, and long names have no `title`.

- [ ] **Step 3: Rebuild the member row, invitations and invite form**

Replace `web/src/app/[workspace]/settings/team/_components/member-row.tsx`:

```tsx
"use client";

import { useActionState } from "react";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { UserAvatar } from "@/components/user-avatar";
import { roleLabels, type Member } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type MemberRowProps = {
  member: Member;
  isSelf: boolean;
  canManage: boolean;
  changeRoles: (state: FormState, formData: FormData) => Promise<FormState>;
  remove: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function MemberRow({ member, isSelf, canManage, changeRoles, remove }: MemberRowProps) {
  const [rolesState, rolesAction, savingRoles] = useActionState(changeRoles, idle);
  const [removeState, removeAction] = useActionState(remove, idle);

  return (
    <li className="flex flex-col gap-3 px-4 py-4 md:px-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center">
        <div className="flex min-w-0 items-center gap-3 lg:w-72 lg:shrink-0">
          <UserAvatar name={member.name} pictureUrl={member.pictureUrl} isYou={isSelf} />
          <div className="min-w-0">
            <p className="flex min-w-0 gap-1 font-semibold text-foreground">
              <span className="truncate" title={member.name}>
                {member.name}
              </span>
              {isSelf && <span className="shrink-0 font-normal text-subtle-foreground">(you)</span>}
            </p>
            <p className="truncate text-small text-subtle-foreground" title={member.email}>
              {member.email}
            </p>
          </div>
        </div>
        {canManage ? (
          <form action={rolesAction} className="flex min-w-0 flex-1 flex-wrap items-center gap-3">
            <RoleCheckboxes legend={`Roles for ${member.name}`} defaultValue={member.roles} />
            <Button type="submit" variant="secondary" size="sm" pending={savingRoles}>
              Save roles
            </Button>
          </form>
        ) : (
          <div className="flex flex-1 flex-wrap gap-1.5">
            {member.roles.map((role) => (
              <Badge key={role}>{roleLabels[role]}</Badge>
            ))}
          </div>
        )}
        {(canManage || isSelf) && (
          <ConfirmDialog
            trigger={
              <Button variant="destructive" size="sm" className="self-start lg:self-auto">
                {isSelf ? "Leave workspace" : "Remove"}
              </Button>
            }
            title={isSelf ? "Leave this workspace?" : `Remove ${member.name}?`}
            description={
              isSelf
                ? "You lose access to this workspace straight away. An admin can invite you again."
                : `${member.name} loses access to this workspace straight away. You can invite them again later.`
            }
            confirmLabel={isSelf ? "Leave workspace" : "Remove"}
            action={removeAction}
          />
        )}
      </div>
      <FormStatus state={rolesState} />
      <FormStatus state={removeState} />
    </li>
  );
}
```

Replace `web/src/app/[workspace]/settings/team/_components/pending-invitations.tsx`:

```tsx
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { roleLabels, type PendingInvitation } from "@/lib/core-api/types";

type PendingInvitationsProps = {
  invitations: PendingInvitation[];
  revoke: (invitationId: string) => Promise<void>;
};

export function PendingInvitations({ invitations, revoke }: PendingInvitationsProps) {
  if (invitations.length === 0) {
    return <p className="text-small text-subtle-foreground">No invitations are waiting.</p>;
  }

  return (
    <ul className="divide-y divide-border">
      {invitations.map((invitation) => (
        <li key={invitation.id} className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0">
          <div className="min-w-0">
            <p className="truncate font-semibold text-foreground" title={invitation.email}>
              {invitation.email}
            </p>
            <p className="text-small text-subtle-foreground">
              {invitation.roles.map((role) => roleLabels[role]).join(", ")} · invited by {invitation.invitedBy}
            </p>
          </div>
          <ConfirmDialog
            trigger={
              <Button variant="destructive" size="sm">
                Revoke
              </Button>
            }
            title={`Revoke the invitation to ${invitation.email}?`}
            description="They won't be able to join with it. You can invite them again later."
            confirmLabel="Revoke"
            action={revoke.bind(null, invitation.id)}
          />
        </li>
      ))}
    </ul>
  );
}
```

Replace `web/src/app/[workspace]/settings/team/_components/invite-form.tsx`:

```tsx
"use client";

import { UserPlus } from "lucide-react";
import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { idle, type FormState } from "@/lib/forms";

type InviteFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function InviteForm({ action }: InviteFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <Field>
        <Label htmlFor="invite-email">Email</Label>
        <Input
          id="invite-email"
          name="email"
          type="email"
          required
          autoComplete="off"
          placeholder="name@company.com"
          aria-describedby="invite-email-error"
          aria-invalid={errors.email ? true : undefined}
        />
        <FieldError id="invite-email-error" errors={errors.email} />
      </Field>
      <Field>
        <p aria-hidden="true" className="text-small font-semibold text-foreground">
          Roles
        </p>
        <RoleCheckboxes legend="Roles for the new member" defaultValue={["marketer"]} />
        <FieldError id="invite-roles-error" errors={errors.roles} />
      </Field>
      <div className="flex flex-col gap-3">
        <Button type="submit" pending={pending} className="self-start max-md:w-full">
          <UserPlus aria-hidden="true" />
          Send invitation
        </Button>
        <FormStatus state={state} />
      </div>
    </form>
  );
}
```

Replace `web/src/app/[workspace]/settings/team/page.tsx`:

```tsx
import type { Metadata } from "next";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getMe, getWorkspace } from "@/lib/core-api/queries";

import { changeRoles, inviteMember, removeMember, revokeInvitation } from "../actions";
import { InviteForm } from "./_components/invite-form";
import { MemberRow } from "./_components/member-row";
import { PendingInvitations } from "./_components/pending-invitations";

export const metadata: Metadata = { title: "Team" };

export default async function TeamPage({ params }: PageProps<"/[workspace]/settings/team">) {
  const { workspace: handle } = await params;
  const [workspace, me] = await Promise.all([getWorkspace(handle), getMe()]);
  const api = await coreApi();
  const brandId = workspace.id;
  const isAdmin = workspace.roles.includes("admin");
  const members = await unwrap(await api.GET("/api/brands/{brandId}/members", { params: { path: { brandId } } }));
  const invitations = isAdmin
    ? await unwrap(await api.GET("/api/brands/{brandId}/invitations", { params: { path: { brandId } } }))
    : [];

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="members-heading" className="flex flex-col gap-3">
        <div>
          <h2 id="members-heading" className="text-base font-semibold text-foreground">
            Members
          </h2>
          <p className="text-small text-subtle-foreground">
            {members.length === 1 ? "1 person can open this workspace." : `${members.length} people can open this workspace.`}
          </p>
        </div>
        <Card className="gap-0 py-0 md:gap-0 md:py-0">
          <ul className="divide-y divide-border">
            {members.map((member) => (
              <MemberRow
                key={member.userId}
                member={member}
                isSelf={member.userId === me.id}
                canManage={isAdmin}
                changeRoles={changeRoles.bind(null, brandId, member.userId)}
                remove={removeMember.bind(null, brandId, member.userId)}
              />
            ))}
          </ul>
        </Card>
      </section>
      {isAdmin && (
        <div className="grid items-start gap-6 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Invite someone</CardTitle>
              <CardDescription>They sign in to Kairo with Google using this email.</CardDescription>
            </CardHeader>
            <CardContent>
              <InviteForm action={inviteMember.bind(null, brandId)} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Waiting invitations</CardTitle>
              <CardDescription>Invitations expire after 14 days.</CardDescription>
            </CardHeader>
            <CardContent>
              <PendingInvitations invitations={invitations} revoke={revokeInvitation.bind(null, brandId)} />
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Rebuild the audit log**

Replace `web/src/app/[workspace]/settings/audit-log/_components/audit-log-table.tsx`:

```tsx
import { ScrollText } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { AuditLogPage } from "@/lib/core-api/types";
import { cn } from "@/lib/utils";

const actions: Record<string, string> = {
  "brand.created": "Created the workspace",
  "brand.updated": "Changed the workspace details",
  "invitation.sent": "Sent an invitation",
  "invitation.revoked": "Revoked an invitation",
  "invitation.accepted": "Accepted an invitation",
  "invitation.declined": "Declined an invitation",
  "member.roles_changed": "Changed a member's roles",
  "member.removed": "Removed a member",
  "member.left": "Left the workspace",
  "approval_settings.changed": "Changed the approval settings",
};

type AuditLogTableProps = {
  page: AuditLogPage;
  timeZone: string;
  olderHref: (before: string) => string;
};

export function AuditLogTable({ page, timeZone, olderHref }: AuditLogTableProps) {
  const format = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });

  if (page.entries.length === 0) {
    return (
      <EmptyState
        icon={ScrollText}
        title="Nothing recorded yet"
        description="Changes to the workspace, the team and approvals will appear here, with who made them and when."
      />
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <Card className="gap-0 overflow-hidden py-0 md:gap-0 md:py-0">
        <Table>
          <TableCaption>Audit log, newest first</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead className="w-40 md:w-48">When</TableHead>
              <TableHead className="w-36 md:w-52">Who</TableHead>
              <TableHead>What</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {page.entries.map((entry) => (
              <TableRow key={String(entry.id)}>
                <TableCell className="text-small text-muted-foreground tabular-nums md:whitespace-nowrap">{format.format(new Date(entry.occurredAt))}</TableCell>
                <TableCell className="font-medium text-foreground">{entry.actorName}</TableCell>
                <TableCell className="text-foreground">{actions[entry.action] ?? entry.action}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
      {page.nextBefore && (
        <Link href={olderHref(page.nextBefore)} className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "self-start")}>
          Older entries
        </Link>
      )}
    </div>
  );
}
```

In `web/src/app/[workspace]/settings/audit-log/page.tsx`, make three changes:
1. Add these imports:

```tsx
import type { Metadata } from "next";

import { Alert } from "@/components/ui/alert";
```

2. Add this line before the default export:

```tsx
export const metadata: Metadata = { title: "Audit log" };
```

3. Replace the line that returns the admin-only `<p>` with:

```tsx
    return <Alert className="max-w-160">Only admins can see the audit log.</Alert>;
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `npm --prefix web test -- "src/app/[workspace]/settings"`
Expected: PASS, including the unchanged audit-log tests ("Neha Rao", "Created the workspace", `/10:00/`, "Older entries").

- [ ] **Step 6: Run the whole suite, then commit**

Run: `npm --prefix web test`
Expected: PASS.

```bash
git add "web/src/app/[workspace]/settings/team" "web/src/app/[workspace]/settings/audit-log"
git commit -m "Redesign team settings with confirmation dialogs, and the audit log"
```

---

### Task 12: Empty states for the areas not built yet

**Files:**
- Modify:
  - `web/src/app/[workspace]/today/page.tsx`
  - `opportunities/page.tsx` and `opportunities/[id]/page.tsx`
  - `campaigns/page.tsx`
  - `campaigns/[id]/layout.tsx` and `campaigns/[id]/page.tsx`
  - `campaigns/[id]/evidence/page.tsx`, `brief/page.tsx`, `assets/page.tsx`, `launch/page.tsx`, `performance/page.tsx`, `results/page.tsx`
  - `triggers/page.tsx`, `impact-lab/page.tsx`, `brand-kit/page.tsx`
- Delete: `web/src/components/page-placeholder.tsx`
- Create: `web/src/app/[workspace]/area-pages.test.tsx`

**Interfaces:**
- Consumes: `PageHeader` and `EmptyState` (Task 6), and `RoomTabs` (existing, now on LinkTabs).

- [ ] **Step 1: Write the failing test**

Create `web/src/app/[workspace]/area-pages.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import type { ComponentType } from "react";
import { expect, test } from "vitest";

import BrandKitPage from "./brand-kit/page";
import AssetsPage from "./campaigns/[id]/assets/page";
import BriefPage from "./campaigns/[id]/brief/page";
import EvidencePage from "./campaigns/[id]/evidence/page";
import LaunchPage from "./campaigns/[id]/launch/page";
import CampaignRoomPage from "./campaigns/[id]/page";
import PerformancePage from "./campaigns/[id]/performance/page";
import ResultsPage from "./campaigns/[id]/results/page";
import CampaignsPage from "./campaigns/page";
import ImpactLabPage from "./impact-lab/page";
import OpportunityPage from "./opportunities/[id]/page";
import OpportunitiesPage from "./opportunities/page";
import TodayPage from "./today/page";
import TriggersPage from "./triggers/page";

test.each([
  ["Today", TodayPage, "Nothing needs you yet"],
  ["Opportunities", OpportunitiesPage, "No opportunities yet"],
  ["Opportunity", OpportunityPage, "Nothing to show yet"],
  ["Campaigns", CampaignsPage, "No campaigns yet"],
  ["Triggers", TriggersPage, "No trigger rules yet"],
  ["Impact Lab", ImpactLabPage, "No analyses yet"],
  ["Brand Kit", BrandKitPage, "No Brand Kit yet"],
] as [string, ComponentType, string][])("%s has one page title and an honest empty state", (title, Page, empty) => {
  render(<Page />);

  expect(screen.getAllByRole("heading", { level: 1 }).map((heading) => heading.textContent)).toEqual([title]);
  expect(screen.getByRole("heading", { level: 2, name: empty })).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
});

test.each([
  ["the room overview", CampaignRoomPage, "Nothing here yet"],
  ["Evidence", EvidencePage, "No evidence yet"],
  ["Brief", BriefPage, "No brief yet"],
  ["Assets", AssetsPage, "No assets yet"],
  ["Launch", LaunchPage, "Not ready to launch"],
  ["Performance", PerformancePage, "No performance yet"],
  ["Results", ResultsPage, "No results yet"],
] as [string, ComponentType, string][])("%s sits under the room's title with its own empty state", (_, Page, empty) => {
  render(<Page />);

  expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
  expect(screen.getByRole("heading", { level: 2, name: empty })).toBeTruthy();
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm --prefix web test -- "src/app/[workspace]/area-pages.test.tsx"`
Expected: FAIL. The placeholders render an `h1` and no empty states.

- [ ] **Step 3: Rewrite the seven area pages**

Replace `web/src/app/[workspace]/today/page.tsx`:

```tsx
import { Sun } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Today" };

export default function TodayPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Today" description="What needs you this morning." />
      <EmptyState
        icon={Sun}
        title="Nothing needs you yet"
        description="Approvals waiting for you, alerts, top opportunities and live tests will show up here each morning."
      />
    </div>
  );
}
```

Replace `web/src/app/[workspace]/opportunities/page.tsx`:

```tsx
import { TrendingUp } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Opportunities" };

export default function OpportunitiesPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Opportunities" description="Rising demand Kairo found for your products, ranked by score." />
      <EmptyState
        icon={TrendingUp}
        title="No opportunities yet"
        description="Every morning before 07:00, Kairo will rank rising searches you can sell into, with the trend, regions, matching products and the reasons behind each score."
      />
    </div>
  );
}
```

Replace `web/src/app/[workspace]/opportunities/[id]/page.tsx`:

```tsx
import { ChartLine } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Opportunity" };

export default function OpportunityPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader eyebrow="Opportunities" title="Opportunity" description="The evidence behind one opportunity." />
      <EmptyState
        icon={ChartLine}
        title="Nothing to show yet"
        description="The search trend, rising regions, matching products, sourced reasons and the score breakdown will appear here."
      />
    </div>
  );
}
```

Replace `web/src/app/[workspace]/campaigns/page.tsx`:

```tsx
import { Megaphone } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Campaigns" };

export default function CampaignsPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Campaigns" description="Every campaign room, from brief to results." />
      <EmptyState
        icon={Megaphone}
        title="No campaigns yet"
        description="Accept an opportunity and Kairo opens a campaign room for it, with the evidence attached and the brief drafting."
      />
    </div>
  );
}
```

Replace `web/src/app/[workspace]/triggers/page.tsx`:

```tsx
import { Zap } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Triggers" };

export default function TriggersPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Triggers" description="Rules that turn weather, festivals and stock into opportunities ready to review." />
      <EmptyState
        icon={Zap}
        title="No trigger rules yet"
        description="A rule watches a forecast, a festival date or a stock level, and drafts an opportunity when its moment comes. Nothing spends money until a person approves it."
      />
    </div>
  );
}
```

Replace `web/src/app/[workspace]/impact-lab/page.tsx`:

```tsx
import { FlaskConical } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Impact Lab" };

export default function ImpactLabPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Impact Lab" description="Measure changes Kairo didn't launch." />
      <EmptyState
        icon={FlaskConical}
        title="No analyses yet"
        description="Describe a change such as a price cut or a TV burst, pick an outcome, and Kairo estimates its effect against data the change didn't touch."
      />
    </div>
  );
}
```

Replace `web/src/app/[workspace]/brand-kit/page.tsx`:

```tsx
import { Palette } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Brand Kit" };

export default function BrandKitPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Brand Kit" description="Your brand's voice, visuals, rules and memory, used by every agent." />
      <EmptyState
        icon={Palette}
        title="No Brand Kit yet"
        description="Kairo will draft your tone of voice, colours, logo, key messages and product categories from your website and best ads, for an admin to approve."
      />
    </div>
  );
}
```

- [ ] **Step 4: Rewrite the campaign room**

Replace `web/src/app/[workspace]/campaigns/[id]/layout.tsx`:

```tsx
import { Suspense } from "react";

import { PageHeader } from "@/components/page-header";

import { RoomTabs } from "./_components/room-tabs";

export default function CampaignRoomLayout({ children, params }: LayoutProps<"/[workspace]/campaigns/[id]">) {
  return (
    <div className="flex flex-col gap-8">
      <div>
        <PageHeader eyebrow="Campaigns" title="Campaign room" description="Where this campaign stands, who acts next, and everything it has produced." />
        <Suspense fallback={<div className="mt-6 h-11 border-b border-border" />}>
          <RoomTabs params={params} />
        </Suspense>
      </div>
      {children}
    </div>
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/page.tsx`:

```tsx
import { ListChecks } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function CampaignRoomPage() {
  return (
    <EmptyState
      icon={ListChecks}
      title="Nothing here yet"
      description="The stage tracker, who owns the next action, and the log of every agent and person's action will appear here."
    />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/evidence/page.tsx`:

```tsx
import { FileSearch } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function EvidencePage() {
  return (
    <EmptyState icon={FileSearch} title="No evidence yet" description="The opportunity's trend, regions, matching products and sources will be attached here." />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/brief/page.tsx`:

```tsx
import { FileText } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function BriefPage() {
  return (
    <EmptyState
      icon={FileText}
      title="No brief yet"
      description="Kairo drafts the brief here, with the reasoning for each field, the budget against your limit, and the test and holdout regions."
    />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/assets/page.tsx`:

```tsx
import { Images } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function AssetsPage() {
  return (
    <EmptyState
      icon={Images}
      title="No assets yet"
      description="Search ads, Demand Gen ads, images and the video will appear here once the brief is approved, each with its Brand Guardian score."
    />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/launch/page.tsx`:

```tsx
import { Rocket } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function LaunchPage() {
  return (
    <EmptyState
      icon={Rocket}
      title="Not ready to launch"
      description="The launch summary, its checks and live progress will appear here after the assets are approved."
    />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/performance/page.tsx`:

```tsx
import { Activity } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function PerformancePage() {
  return (
    <EmptyState
      icon={Activity}
      title="No performance yet"
      description="Pacing against plan, daily spend and results, early readings and the pause log will appear here once the campaign is live."
    />
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/results/page.tsx`:

```tsx
import { ChartColumn } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function ResultsPage() {
  return (
    <EmptyState
      icon={ChartColumn}
      title="No results yet"
      description="The impact report, with the lift, its range and the recommended next step, will appear here when the test ends."
    />
  );
}
```

- [ ] **Step 5: Remove the placeholder component**

```bash
git rm web/src/components/page-placeholder.tsx
```

Run: `git grep -n "page-placeholder" -- web/src`
Expected: no output.

- [ ] **Step 6: Run the test and watch it pass**

Run: `npm --prefix web test -- "src/app/[workspace]/area-pages.test.tsx"`
Expected: PASS, 14 tests.

If an icon import fails, Lucide may have renamed it. Run `node -e "console.log(Object.keys(require('./web/node_modules/lucide-react')).filter(k=>/^(ChartLine|ChartColumn|ListChecks|FileSearch|Images|Rocket|Activity)$/.test(k)))"`, and use the listed names.

- [ ] **Step 7: Run the whole suite and the build, then commit**

Run: `npm --prefix web test`, then `npm --prefix web run build`
Expected: both pass.

```bash
git add "web/src/app/[workspace]" web/src/components
git commit -m "Replace the placeholder pages with honest empty states"
```

---

### Task 13: Not-found and error pages

**Files:**
- Create: `web/src/components/not-found-state.tsx`, `web/src/components/error-state.tsx`, `web/src/components/status-states.test.tsx`
- Create: `web/src/app/not-found.tsx`, `web/src/app/error.tsx`, `web/src/app/[workspace]/not-found.tsx`, `web/src/app/[workspace]/error.tsx`

**Interfaces:**
- Consumes: `EmptyState` and `SiteHeader` (Task 6), and `Button` and `buttonVariants` (Task 3).
- Produces: `NotFoundState()` and `ErrorState({ retry: () => void })`.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/status-states.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { ErrorState } from "@/components/error-state";
import { NotFoundState } from "@/components/not-found-state";

test("the not-found state is the page heading and leads home", () => {
  render(<NotFoundState />);

  expect(screen.getByRole("heading", { level: 1, name: "We couldn't find that page" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Go to Kairo" }).getAttribute("href")).toBe("/");
});

test("the error state retries when asked", () => {
  const retry = vi.fn();
  render(<ErrorState retry={retry} />);

  expect(screen.getByRole("heading", { level: 1, name: "Something went wrong" })).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));

  expect(retry).toHaveBeenCalledOnce();
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm --prefix web test -- src/components/status-states.test.tsx`
Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Write the states and the pages**

Create `web/src/components/not-found-state.tsx`:

```tsx
import { Compass } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";

export function NotFoundState() {
  return (
    <EmptyState as="h1" icon={Compass} title="We couldn't find that page" description="It may have moved, or you may not have access to it.">
      <Link href="/" className={buttonVariants()}>
        Go to Kairo
      </Link>
    </EmptyState>
  );
}
```

Create `web/src/components/error-state.tsx`:

```tsx
"use client";

import { RotateCcw, TriangleAlert } from "lucide-react";

import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";

export function ErrorState({ retry }: { retry: () => void }) {
  return (
    <EmptyState
      as="h1"
      icon={TriangleAlert}
      title="Something went wrong"
      description="Kairo couldn't load this page. Your work is saved. Try again, and if it keeps happening, tell your admin."
    >
      <Button onClick={() => retry()}>
        <RotateCcw aria-hidden="true" />
        Try again
      </Button>
    </EmptyState>
  );
}
```

Create `web/src/app/not-found.tsx`:

```tsx
import { NotFoundState } from "@/components/not-found-state";
import { SiteHeader } from "@/components/site-header";

export default function NotFound() {
  return (
    <div className="min-h-dvh bg-background">
      <SiteHeader />
      <main className="mx-auto w-full max-w-2xl px-4 py-12 md:py-20">
        <NotFoundState />
      </main>
    </div>
  );
}
```

Create `web/src/app/error.tsx`:

```tsx
"use client";

import { ErrorState } from "@/components/error-state";
import { SiteHeader } from "@/components/site-header";

export default function RootError({ retry }: { retry: () => void }) {
  return (
    <div className="min-h-dvh bg-background">
      <SiteHeader />
      <main className="mx-auto w-full max-w-2xl px-4 py-12 md:py-20">
        <ErrorState retry={retry} />
      </main>
    </div>
  );
}
```

Create `web/src/app/[workspace]/not-found.tsx`:

```tsx
import { NotFoundState } from "@/components/not-found-state";

export default function WorkspaceNotFound() {
  return <NotFoundState />;
}
```

Create `web/src/app/[workspace]/error.tsx`:

```tsx
"use client";

import { ErrorState } from "@/components/error-state";

export default function WorkspaceError({ retry }: { retry: () => void }) {
  return <ErrorState retry={retry} />;
}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `npm --prefix web test -- src/components/status-states.test.tsx`
Expected: PASS, 2 tests.

- [ ] **Step 5: Run the whole suite and the build, then commit**

Run: `npm --prefix web test`, then `npm --prefix web run build`
Expected: both pass.

```bash
git add web/src/components/not-found-state.tsx web/src/components/error-state.tsx web/src/components/status-states.test.tsx web/src/app/not-found.tsx web/src/app/error.tsx "web/src/app/[workspace]/not-found.tsx" "web/src/app/[workspace]/error.tsx"
git commit -m "Add not-found and error pages in the new style"
```

---

### Task 14: Documentation

**Files:**
- Modify: `web/README.md`, `docs/Kairo-SAD.md`, `docs/design.md`, `docs/superpowers/specs/2026-10-13-design-system-design.md`

- [ ] **Step 1: Point the web README at the design system**

In `web/README.md`, insert this paragraph immediately after the sentence that ends "It talks only to the core API.":

```markdown
**Design.** Every screen follows [the design system](../docs/design.md). Primitives live in `src/components/ui/` (shadcn/ui on Base UI, restyled to the tokens), and Kairo's own composites, such as the shell, page header and empty state, live in `src/components/`. Forms use native controls, and Base UI is used only for menus, sheets and dialogs.
```

- [ ] **Step 2: Name the component library in the SAD**

In `docs/Kairo-SAD.md` §2, change the web app row's technology cell from `Next.js and React in TypeScript, Tailwind CSS, SignalR client` to:

```markdown
Next.js and React in TypeScript, Tailwind CSS, shadcn/ui on Base UI, SignalR client
```

- [ ] **Step 3: Match the design doc to what was built**

In `docs/design.md` §11:
- Change the first row of the implementation table to: `| Tokens (light in \`:root\`, dark in the \`prefers-color-scheme: dark\` block, mapped to Tailwind with \`@theme\`), and the \`no-scrollbar\` utility | \`web/src/app/globals.css\` |`. Base UI's `data-open`, `data-closed` and other state attributes need no custom variants: Tailwind matches `data-*` attributes natively.
- Add this bullet to "Rules for code": `- Third-party marks, such as the Google G on the sign-in button, keep their official colours.`

- [ ] **Step 4: Match the spec's file list to what was built**

In `docs/superpowers/specs/2026-10-13-design-system-design.md`, in the Files table:
- Change the Primitives cell to: `button, input, native-select, checkbox, choice-chip, label, field, card, badge, alert, avatar, table, dropdown-menu, sheet, alert-dialog, skeleton`.
- Change the Composites cell to: `logo, app-shell, mobile-nav, skeletons, nav-menu, workspace-switcher, user-menu, user-avatar, page-header, link-tabs, empty-state, site-header, confirm-dialog, not-found-state, error-state, form-status, field-error, role-checkboxes, sign-out-button. \`page-placeholder\` is removed.`
- In "Behaviour changes that update tests", add the row: `| The sign-in link reads "Continue with Google" | \`sign-in-panel.test.tsx\` uses the new name |`.

- [ ] **Step 5: Commit**

```bash
git add web/README.md docs/Kairo-SAD.md docs/design.md docs/superpowers/specs/2026-10-13-design-system-design.md
git commit -m "Point the web README and the SAD at the design system"
```

---

### Task 15: Visual check in a real browser (scratchpad only, never committed)

This task checks layout, overflow, contrast and focus on real screens. Unit tests can't. Everything here lives in the session scratchpad. `$SCRATCH` below means the scratchpad directory from the session's environment.

**Files (scratchpad, not committed):**
- Create: `$SCRATCH/harness/package.json`, `$SCRATCH/harness/mock-core.mjs`, `$SCRATCH/harness/shoot.mjs`

- [ ] **Step 1: Set up the harness**

```bash
mkdir -p "$SCRATCH/harness" "$SCRATCH/shots"
cd "$SCRATCH/harness" && npm init -y >/dev/null && PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm install --no-audit --no-fund playwright@1
```

- [ ] **Step 2: Write the mock of the core API**

Create `$SCRATCH/harness/mock-core.mjs`:

```js
import http from "node:http";

const me = { id: "u1", name: "Neha Kapoor", email: "neha@acme.example", pictureUrl: null };
const brands = [
  { id: "b1", handle: "acme", name: "Acme Footwear", roles: ["admin", "marketer", "approver"] },
  { id: "b2", handle: "monsoon", name: "Monsoon Running Company and Rain Accessories Private Limited", roles: ["marketer"] },
];
const members = [
  { userId: "u1", name: "Neha Kapoor", email: "neha@acme.example", pictureUrl: null, roles: ["admin"], joinedAt: "2026-10-01T00:00:00Z" },
  { userId: "u2", name: "Priya Shah", email: "priya@acme.example", pictureUrl: null, roles: ["marketer", "approver"], joinedAt: "2026-10-02T00:00:00Z" },
  { userId: "u3", name: "Priyadarshini Venkataraghavan Subramanian-Iyer", email: "priyadarshini.venkataraghavan@acme-footwear.example", pictureUrl: null, roles: ["approver"], joinedAt: "2026-10-03T00:00:00Z" },
];
const pending = [
  { id: "i1", email: "meera@acme.example", roles: ["approver"], invitedBy: "Neha Kapoor", sentAt: "2026-10-12T00:00:00Z", expiresAt: "2026-10-26T00:00:00Z" },
  { id: "i2", email: "kabir@agency.example", roles: ["marketer"], invitedBy: "Neha Kapoor", sentAt: "2026-10-12T00:00:00Z", expiresAt: "2026-10-26T00:00:00Z" },
];
const auditEntries = [
  ["2026-10-13T04:30:00Z", "Neha Kapoor", "approval_settings.changed"],
  ["2026-10-13T04:10:00Z", "Neha Kapoor", "member.roles_changed"],
  ["2026-10-12T11:05:00Z", "Priya Shah", "invitation.accepted"],
  ["2026-10-12T10:00:00Z", "Neha Kapoor", "invitation.sent"],
  ["2026-10-12T09:00:00Z", "Neha Kapoor", "brand.created"],
].map(([occurredAt, actorName, action], index) => ({ id: index + 1, occurredAt, actorType: "user", actorId: "u1", actorName, action, targetType: "brand", targetId: "b1", before: null, after: null }));

const routes = [
  [/^\/api\/me$/, () => me],
  [/^\/api\/me\/invitations$/, () => [{ id: "mi1", workspaceName: "Rainy Day Sandals", workspaceHandle: "rainy", roles: ["marketer", "approver"], invitedBy: "Arjun Mehta", expiresAt: "2026-10-26T00:00:00Z" }]],
  [/^\/api\/brands$/, () => brands],
  [/^\/api\/brands\/(b1|b2)$/, (id) => ({ ...brands.find((brand) => brand.id === id), timeZone: "Asia/Kolkata", currency: "INR" })],
  [/^\/api\/brands\/(b1|b2)\/members$/, () => members],
  [/^\/api\/brands\/(b1|b2)\/invitations$/, () => pending],
  [/^\/api\/brands\/(b1|b2)\/approval-settings$/, () => ({ briefApprovers: ["u2"], assetApprovers: ["u2", "u3"], launchApprovers: ["u2"], budgetOwners: ["u3"], budgetLimit: 200000, currency: "INR" })],
  [/^\/api\/brands\/(b1|b2)\/audit-log$/, () => ({ entries: auditEntries, nextBefore: "5" })],
];

http
  .createServer((request, response) => {
    const path = new URL(request.url, "http://localhost").pathname;
    for (const [pattern, handler] of routes) {
      const match = path.match(pattern);
      if (match) {
        response.writeHead(200, { "content-type": "application/json" });
        response.end(JSON.stringify(handler(match[1])));
        return;
      }
    }
    response.writeHead(404, { "content-type": "application/problem+json" });
    response.end(JSON.stringify({ title: "Not Found", status: 404 }));
  })
  .listen(4100, () => console.log("mock core on 4100"));
```

- [ ] **Step 3: Write the screenshot script**

Create `$SCRATCH/harness/shoot.mjs`:

```js
import { chromium } from "playwright";

const base = "http://localhost:3100";
const out = process.argv[2];
const pages = [
  ["sign-in", "/sign-in", false],
  ["sign-in-error", "/sign-in?error=sign_in_failed", false],
  ["welcome", "/welcome", true],
  ["today", "/acme/today", true],
  ["opportunities", "/acme/opportunities", true],
  ["campaign-brief", "/acme/campaigns/42/brief", true],
  ["settings", "/acme/settings", true],
  ["team", "/acme/settings/team", true],
  ["approvals", "/acme/settings/approvals", true],
  ["audit-log", "/acme/settings/audit-log", true],
  ["settings-not-admin", "/monsoon/settings", true],
  ["not-found", "/acme/nowhere/at/all", true],
];
const widths = [320, 375, 768, 1440];
const schemes = ["light", "dark"];
const problems = [];

const browser = await chromium.launch({ channel: "msedge" });
for (const scheme of schemes) {
  for (const width of widths) {
    const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme: scheme, reducedMotion: "reduce" });
    await context.addCookies([{ name: "__Host-kairo-session", value: "harness", url: base, secure: true, httpOnly: true, sameSite: "Lax" }]);
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`${scheme} ${width} pageerror ${error.message}`));
    for (const [name, path] of pages) {
      await page.goto(base + path, { waitUntil: "networkidle" });
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      if (overflow > 0) problems.push(`${scheme} ${width} ${name} overflows by ${overflow}px`);
      await page.screenshot({ path: `${out}/${scheme}-${width}-${name}.png`, fullPage: true });
    }
    if (width === 375) {
      await page.goto(`${base}/acme/settings/team`, { waitUntil: "networkidle" });
      await page.getByRole("button", { name: "Open navigation" }).click();
      await page.getByRole("dialog", { name: "Navigation" }).waitFor();
      await page.screenshot({ path: `${out}/${scheme}-${width}-drawer.png` });
    }
    if (width === 1440) {
      await page.goto(`${base}/acme/settings/team`, { waitUntil: "networkidle" });
      await page.getByRole("button", { name: /^Workspace:/ }).click();
      await page.getByRole("menu", { name: "Workspaces" }).waitFor();
      await page.screenshot({ path: `${out}/${scheme}-${width}-workspace-menu.png` });
      await page.keyboard.press("Escape");
      await page.getByRole("button", { name: "Remove" }).first().click();
      await page.getByRole("alertdialog").waitFor();
      await page.screenshot({ path: `${out}/${scheme}-${width}-remove-dialog.png` });
      await page.keyboard.press("Escape");
      await page.goto(`${base}/acme/settings`, { waitUntil: "networkidle" });
      for (let step = 0; step < 4; step += 1) await page.keyboard.press("Tab");
      await page.screenshot({ path: `${out}/${scheme}-${width}-focus.png` });
    }
    await context.close();
  }
}
await browser.close();
console.log(problems.length ? problems.join("\n") : "no overflow or page errors");
```

- [ ] **Step 4: Start the mock core and the web app**

Run in the background: `node "$SCRATCH/harness/mock-core.mjs"`

Run in the background: `KAIRO_API_URL=http://localhost:4100 npm --prefix web run dev -- --port 3100`

Wait until `http://localhost:3100/sign-in` answers with status 200.

- [ ] **Step 5: Take the screenshots**

Run: `node "$SCRATCH/harness/shoot.mjs" "$SCRATCH/shots"`
Expected: "no overflow or page errors". Any line it prints is a defect to fix.

- [ ] **Step 6: Review every screenshot**

Open each PNG in `$SCRATCH/shots` and check it against `docs/design.md`:
- **Layout:** the serif appears only at display sizes, and there is one terracotta button per view.
- **Text:** long names truncate, and every text is readable in dark mode.
- **Interaction:** the focus ring is visible, and the drawer, menu and dialog look as the design describes.
- **Structure:** empty states are honest, and nothing overlaps or is clipped.

Write down every defect.

- [ ] **Step 7: Fix the defects**

For each defect:
1. Fix it in the owning component.
2. Add or adjust a unit test where the behaviour is testable.
3. Run `npm --prefix web test`.
4. Commit with a message naming the fix, such as "Stop long workspace names widening the phone top bar".

Then re-run Step 5 until it reports no problems and the screenshots look right.

- [ ] **Step 8: Stop the background processes**

Stop the `next dev` process and the mock core.

---

### Task 16: Final checks, review and integration

- [ ] **Step 1: Run every web check from a clean state**

```bash
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
npm --prefix web run generate:api
git status --porcelain web/src/lib/core-api/schema.ts
```

Expected: lint, tests and build pass, and the last command prints nothing.

- [ ] **Step 2: Have the whole branch reviewed**

Use superpowers:requesting-code-review on the range from `develop` to `feature/design-system`. The reviewer checks it against `docs/design.md` and the spec. Fix what the review finds, with tests, and commit each fix.

- [ ] **Step 3: Push the branch and watch CI**

```bash
git push -u origin feature/design-system
gh run list --branch feature/design-system --limit 1
```

Watch the run with `gh run watch <run id>` until it passes.

- [ ] **Step 4: Integrate into `develop`**

Use superpowers:finishing-a-development-branch, and follow the repository's workflow for `develop`.

- [ ] **Step 5: Mark the task complete on `develop`**

Once the work is on `origin/develop`:
1. Change `- [/] **Design system**` to `- [x] **Design system**` in `docs/task.md`.
2. Commit only that file with the message "Mark the design system task complete".
3. Push to `origin/develop`.
