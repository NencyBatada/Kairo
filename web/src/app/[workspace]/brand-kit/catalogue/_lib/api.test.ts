import { afterEach, expect, test, vi } from "vitest";

import { catalogueRequest } from "./api";

vi.mock("server-only", () => ({}));
vi.mock("next/headers", () => ({ cookies: async () => ({ get: () => ({ value: "test-session" }) }) }));
vi.mock("@/lib/core-api/identity-token", () => ({ identityToken: async () => "identity" }));
afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); });
test("scopes requests to the brand and forwards session, service identity and mutation headers", async () => {
  vi.stubEnv("KAIRO_API_URL", "https://api.example");
  const fetch = vi.fn(async () => new Response("{}")); vi.stubGlobal("fetch", fetch);
  await catalogueRequest("brand-1", "/feed", { method: "PUT", body: JSON.stringify({ url: "https://shop.example/feed" }) });
  const [url, options] = fetch.mock.calls[0] as unknown as [string, RequestInit];
  expect(url).toBe("https://api.example/api/brands/brand-1/catalogue/feed");
  const headers = new Headers(options.headers);
  expect(headers.get("Cookie")).toBe("__Host-kairo-session=test-session");
  expect(headers.get("X-Serverless-Authorization")).toBe("Bearer identity");
  expect(headers.get("X-Kairo-Request")).toBe("1");
  expect(headers.get("Content-Type")).toBe("application/json");
  expect(options.cache).toBe("no-store");
});
