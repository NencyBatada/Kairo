import { defineConfig, devices } from "@playwright/test";
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { fixtureData } from "./e2e/fixtures";

mkdirSync(".cache/accessibility", { recursive: true });
writeFileSync(".cache/accessibility/fixture-data.json", JSON.stringify(fixtureData()));
if (!existsSync(".cache/accessibility/cert.pem")) {
  const openssl = process.platform === "win32" && existsSync("C:/Program Files/Git/usr/bin/openssl.exe") ? "C:/Program Files/Git/usr/bin/openssl.exe" : "openssl";
  execFileSync(openssl, ["req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", ".cache/accessibility/key.pem", "-out", ".cache/accessibility/cert.pem", "-days", "2", "-subj", "/CN=localhost", "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1"], { stdio: "ignore" });
}

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  workers: process.env.CI ? 2 : 3,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: { baseURL: "https://127.0.0.1:3214", ignoreHTTPSErrors: true, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "firefox", use: { ...devices["Desktop Firefox"] } },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
  ],
  webServer: [
    { command: "node e2e/fixture-api.mjs", url: "http://127.0.0.1:4214/health", reuseExistingServer: false, timeout: 30_000 },
    { command: "node e2e/start-web.mjs", url: "https://127.0.0.1:3214/health", ignoreHTTPSErrors: true, reuseExistingServer: false, timeout: 60_000,
      env: { KAIRO_API_URL: "http://127.0.0.1:4214", KAIRO_API_AUDIENCE: "", OTEL_SDK_DISABLED: "true" } },
  ],
});
