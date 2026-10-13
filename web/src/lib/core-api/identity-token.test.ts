import { afterEach, beforeEach, expect, test, vi } from "vitest";

vi.mock("server-only", () => ({}));

const AUDIENCE = "https://kairo-api-123.asia-south1.run.app";

function tokenExpiringAt(seconds: number) {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "RS256" })}.${encode({ exp: seconds })}.signature`;
}

async function load() {
  vi.resetModules();
  return (await import("./identity-token")).identityToken;
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-10-13T10:00:00Z"));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

test("returns nothing and calls nobody when no audience is set", async () => {
  const fetch = vi.fn();
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_AUDIENCE", "");

  expect(await (await load())()).toBeUndefined();
  expect(fetch).not.toHaveBeenCalled();
});

test("asks the metadata server for a token for the audience", async () => {
  const token = tokenExpiringAt(Date.parse("2026-10-13T11:00:00Z") / 1000);
  const fetch = vi.fn(async () => new Response(token));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_AUDIENCE", AUDIENCE);

  expect(await (await load())()).toBe(token);
  expect(fetch).toHaveBeenCalledWith(
    `http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity?audience=${encodeURIComponent(AUDIENCE)}`,
    expect.objectContaining({ headers: { "Metadata-Flavor": "Google" } }),
  );
});

test("reuses the token until five minutes before it expires", async () => {
  const first = tokenExpiringAt(Date.parse("2026-10-13T11:00:00Z") / 1000);
  const second = tokenExpiringAt(Date.parse("2026-10-13T12:00:00Z") / 1000);
  const fetch = vi.fn().mockResolvedValueOnce(new Response(first)).mockResolvedValueOnce(new Response(second));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_AUDIENCE", AUDIENCE);
  const identityToken = await load();

  expect(await identityToken()).toBe(first);
  vi.setSystemTime(new Date("2026-10-13T10:54:00Z"));
  expect(await identityToken()).toBe(first);
  vi.setSystemTime(new Date("2026-10-13T10:56:00Z"));
  expect(await identityToken()).toBe(second);
  expect(fetch).toHaveBeenCalledTimes(2);
});

test("fails loudly when the metadata server refuses", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => new Response("no", { status: 404 })));
  vi.stubEnv("KAIRO_API_AUDIENCE", AUDIENCE);

  await expect((await load())()).rejects.toThrow("The metadata server answered 404 for an identity token.");
});
