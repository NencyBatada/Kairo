import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { routes } from "./fixtures";

test.beforeEach(async ({ context }) => {
  await context.addCookies([{ name: "__Host-kairo-session", value: "fixture-editor", url: "https://127.0.0.1:3214/", httpOnly: true, secure: true, sameSite: "Lax" }]);
});

test.afterEach(async ({ request }) => {
  const response = await request.get("http://127.0.0.1:4214/__fixture/missing");
  expect(await response.json(), "Every server request must have an explicit API fixture").toEqual([]);
});

for (const theme of ["light", "dark"] as const) {
  for (const route of routes) {
    test(`${theme} ${route}`, async ({ page, browserName }, testInfo) => {
      test.skip(browserName !== "chromium" && !["/sign-in", "/welcome", "/acme/today", "/acme/settings", "/acme/opportunities", ...["brief", "review", "launch", "assets"].map(tab => `/acme/campaigns/22222222-2222-2222-2222-222222222222/${tab}`)].includes(route), "Full route matrix runs in Chromium; critical journeys also run in Firefox and WebKit.");
      const errors: string[] = [];
      page.on("pageerror", error => errors.push(error.message));
      page.on("console", message => { if (message.type() === "error" && /Server Components render|TypeError|Cannot read properties/.test(message.text())) errors.push(message.text()); });
      await page.emulateMedia({ colorScheme: theme, reducedMotion: "reduce" });
      const response = await page.goto(route);
      expect(response?.status()).toBe(200);
      await expect(page.locator("main h1")).toHaveCount(1);
      await expect(page.locator('[data-slot="skeleton"]')).toHaveCount(0);
      await expect(page.getByRole("alert").filter({ hasText: /could not|couldn't|couldn’t|unable to|something went wrong|try again/i })).toHaveCount(0);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
      await testInfo.attach("axe", { body: JSON.stringify({ route, theme, violations: results.violations, incomplete: results.incomplete }, null, 2), contentType: "application/json" });
      expect(results.violations).toEqual([]);
      expect(errors, "Healthy routes must not hide server-rendering or hydration errors").toEqual([]);
    });
  }
}

test("first Tab reaches the skip link", async ({ page, browserName }) => {
  test.skip(browserName === "webkit" && process.platform === "win32", "The tested Windows WebKit port skips links on Tab; verify native Safari keyboard preferences and traversal manually.");
  await page.goto("/acme/today");
  await expect(page.locator("main h1")).toHaveCount(1);
  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to content" })).toBeFocused();
});

test("skip link activation moves focus to main; mobile navigation restores focus", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto("/acme/today");
  await expect(page.locator("main h1")).toHaveCount(1);
  await page.bringToFront();
  await page.getByRole("link", { name: "Skip to content" }).focus();
  await expect(page.getByRole("link", { name: "Skip to content" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("main")).toBeFocused();
  const trigger = page.getByRole("button", { name: /open navigation/i });
  await trigger.click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
});

for (const route of ["/welcome", "/acme/opportunities", `/acme/campaigns/22222222-2222-2222-2222-222222222222/brief`, "/acme/brand-kit", "/acme/settings", "/acme/impact-lab/new"]) {
  test(`reflow and text spacing ${route}`, async ({ page, browserName }, testInfo) => {
    test.skip(browserName !== "chromium", "Layout matrix uses Chromium; keyboard journeys cover every engine.");
    await page.setViewportSize({ width: 320, height: 800 });
    await page.emulateMedia({ reducedMotion: "reduce", colorScheme: "dark" });
    await page.goto(route);
    await expect(page.locator("main h1")).toHaveCount(1);
    await expect(page.locator('[data-slot="skeleton"]')).toHaveCount(0);
    await page.addStyleTag({ content: "* { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } p { margin-bottom: 2em !important; }" });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), "320px reflow must not create page-wide horizontal scrolling").toBe(true);
    await testInfo.attach("320px-text-spacing", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.addStyleTag({ content: "html { font-size: 200% !important; }" });
    const overflow = await page.evaluate(() => [...document.querySelectorAll("body *")].map(element => ({ tag: element.tagName, classes: element.className?.toString(), text: element.textContent?.slice(0, 80), right: element.getBoundingClientRect().right, width: element.getBoundingClientRect().width })).filter(item => item.right > window.innerWidth + 1 && item.width > 0).slice(0, 20));
    if (overflow.length) await testInfo.attach("overflow-diagnostic", { body: JSON.stringify(overflow, null, 2), contentType: "application/json" });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `200% text must remain within the page: ${JSON.stringify(overflow)}`).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"]).analyze()).violations).toEqual([]);
  });
}

for (const state of ["empty", "viewer", "loading", "error"] as const) {
  test(`${state} opportunity screen`, async ({ page, context, browserName }, testInfo) => {
    test.skip(browserName !== "chromium", "State matrix runs in Chromium.");
    await context.addCookies([{ name: "__Host-kairo-session", value: `fixture-${state}`, url: "https://127.0.0.1:3214/", httpOnly: true, secure: true, sameSite: "Lax" }]);
    await page.goto("/acme/opportunities", { waitUntil: state === "loading" ? "commit" : "load" });
    if (state === "loading") await expect(page.getByRole("status").filter({ hasText: /^Loading$/ })).toBeVisible();
    if (state === "error") await expect(page.getByRole("alert")).toBeVisible();
    else {
      await expect(page.getByRole("heading", { name: "Opportunities", exact: true })).toBeVisible();
      await expect(page.locator('[data-slot="skeleton"]')).toHaveCount(0);
      if (state === "empty") await expect(page.getByText("No opportunities yet", { exact: true })).toBeVisible();
    }
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"]).analyze();
    await testInfo.attach("state-axe", { body: JSON.stringify(results.violations), contentType: "application/json" });
    expect(results.violations).toEqual([]);
  });
}

test("Ask Kairo supports keyboard opening, dismissal and focus restoration in both layouts", async ({ page }) => {
  for (const width of [1440, 375]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/acme/today");
    await expect(page.locator("main h1")).toHaveCount(1);
    const trigger = page.locator("#ask-kairo-trigger");
    await trigger.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByLabel("Your question")).toBeVisible();
    if (width === 1440) {
      await expect(page.getByRole("heading", { name: "Ask Kairo", exact: true })).toBeFocused();
      await page.getByRole("button", { name: "Close", exact: true }).click();
    } else {
      await expect(page.getByRole("dialog", { name: "Ask Kairo" })).toBeVisible();
      await page.keyboard.press("Escape");
    }
    await expect(trigger).toBeFocused();
  }
});

test("media alternatives expose captions, transcripts and labelled editors without false completion", async ({ page, context }) => {
  await context.addCookies([{ name: "__Host-kairo-session", value: "fixture-media", url: "https://127.0.0.1:3214/", httpOnly: true, secure: true, sameSite: "Lax" }]);
  await page.goto("/acme/campaigns/22222222-2222-2222-2222-222222222222/assets");
  await expect(page.getByRole("img", { name: "A solid red test square." })).toBeVisible();
  await expect(page.getByText("Draft alternatives · Not yet reviewed against this media.")).toHaveCount(2);
  const video = page.locator("video");
  await expect(video).toHaveAttribute("controls", "");
  await expect(video.locator("track")).toHaveAttribute("src", /file=captions&revision=1&sourceHash=/);
  const transcript = page.getByText("Read transcript", { exact: true });
  await transcript.focus(); await page.keyboard.press("Enter");
  await expect(page.locator("p").filter({ hasText: "A solid red square stays still for three seconds. There is no audio." })).toBeVisible();
  await page.getByText("Edit media alternatives", { exact: true }).first().click();
  const alt = page.getByLabel("Image description");
  await alt.fill("An edited description retained after a failed save.");
  await page.getByRole("button", { name: "Save draft alternatives" }).first().click();
  await expect(page.getByRole("alert").filter({ hasText: "Fixture action rejected" })).toBeVisible();
  await expect(alt).toHaveValue("An edited description retained after a failed save.");
  await expect(page.getByRole("button", { name: "Confirm alternatives reviewed" }).first()).toBeDisabled();
  expect((await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"]).analyze()).violations).toEqual([]);
});
