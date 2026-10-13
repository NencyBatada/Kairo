import { afterEach, beforeEach, expect, test, vi } from "vitest";

import { GET, POST } from "./route";

vi.mock("server-only", () => ({}));
vi.mock("@/lib/core-api/identity-token", () => ({ identityToken: vi.fn(async () => undefined) }));

let fetch: ReturnType<typeof vi.fn<(url: URL, init: RequestInit) => Promise<Response>>>;

beforeEach(() => {
  fetch = vi.fn(async () => new Response("ok"));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_URL", "https://core.test");
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

test("forwards the method, path, query, headers and body to the core", async () => {
  await POST(
    new Request("https://web.test/api/brands/7/invitations?x=1", {
      method: "POST",
      headers: { cookie: "__Host-kairo-session=abc", "content-type": "application/json", "X-Kairo-Request": "1", host: "web.test" },
      body: JSON.stringify({ email: "a@b.c" }),
    }),
  );

  const [url, init] = fetch.mock.calls[0];
  expect(url.toString()).toBe("https://core.test/api/brands/7/invitations?x=1");
  expect(init.method).toBe("POST");
  expect(init.redirect).toBe("manual");
  const headers = new Headers(init.headers);
  expect(headers.get("cookie")).toBe("__Host-kairo-session=abc");
  expect(headers.get("x-kairo-request")).toBe("1");
  expect(headers.has("host")).toBe(false);
  expect(new TextDecoder().decode(init.body as ArrayBuffer)).toBe('{"email":"a@b.c"}');
});

test("sends no body with a GET", async () => {
  await GET(new Request("https://web.test/api/me"));

  expect(fetch.mock.calls[0][1].body).toBeUndefined();
});

test("adds the identity token when deployed", async () => {
  const { identityToken } = await import("@/lib/core-api/identity-token");
  vi.mocked(identityToken).mockResolvedValueOnce("signed-token");

  await GET(new Request("https://web.test/api/me"));

  expect(new Headers(fetch.mock.calls[0][1].headers).get("X-Serverless-Authorization")).toBe("Bearer signed-token");
});

test("passes redirects back instead of following them", async () => {
  fetch.mockResolvedValueOnce(new Response(null, { status: 302, headers: { location: "https://accounts.google.com/o/oauth2/v2/auth?x=1" } }));

  const response = await GET(new Request("https://web.test/api/auth/sign-in"));

  expect(response.status).toBe(302);
  expect(response.headers.get("location")).toBe("https://accounts.google.com/o/oauth2/v2/auth?x=1");
});

test("keeps every Set-Cookie header", async () => {
  const headers = new Headers();
  headers.append("set-cookie", "__Host-kairo-session=abc; Path=/; Secure; HttpOnly");
  headers.append("set-cookie", ".AspNetCore.Correlation=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT");
  fetch.mockResolvedValueOnce(new Response(null, { status: 302, headers }));

  const response = await GET(new Request("https://web.test/api/auth/callback/google?code=1"));

  expect(response.headers.getSetCookie()).toEqual([
    "__Host-kairo-session=abc; Path=/; Secure; HttpOnly",
    ".AspNetCore.Correlation=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT",
  ]);
});

test("drops the encoding headers of a body fetch has already decoded", async () => {
  fetch.mockResolvedValueOnce(
    new Response("{}", { headers: { "content-encoding": "gzip", "content-length": "20", "content-type": "application/json" } }),
  );

  const response = await GET(new Request("https://web.test/api/me"));

  expect(response.headers.has("content-encoding")).toBe(false);
  expect(response.headers.has("content-length")).toBe(false);
  expect(response.headers.get("content-type")).toBe("application/json");
});
