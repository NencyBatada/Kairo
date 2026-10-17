import { NextRequest } from "next/server";
import { expect, test } from "vitest";

import { proxy } from "@/proxy";

type WithCookies = Response & { cookies: { get(name: string): { value: string } | undefined } };

function request(path: string, session = false) {
  const req = new NextRequest(new URL(path, "http://localhost:8000"));
  if (session) {
    req.cookies.set("__Host-kairo-session", "abc");
  }

  return req;
}

test("sends visitors without a session to sign-in, remembering where they were going", () => {
  const response = proxy(request("/acme/today?tab=open"));

  expect(response.status).toBe(307);
  expect(response.headers.get("location")).toBe("http://localhost:8000/sign-in?returnUrl=%2Facme%2Ftoday%3Ftab%3Dopen");
});

test("lets the sign-in page through without a session", () => {
  const response = proxy(request("/sign-in"));

  expect(response.status).toBe(200);
});

test("PDF routes require the normal session and preserve the report return target", () => {
  const path = "/acme/campaigns/22222222-2222-2222-2222-222222222222/results/report.pdf?reportId=report";
  expect(proxy(request(path)).status).toBe(307);
  expect(proxy(request(path)).headers.get("location")).toContain(encodeURIComponent(path));
  expect(proxy(request(path, true)).status).toBe(200);
});

test("remembers the workspace a signed-in user opened", () => {
  const response = proxy(request("/acme/settings/team", true)) as WithCookies;

  expect(response.cookies.get("kairo-last-workspace")?.value).toBe("acme");
});

test("does not treat top-level pages as workspaces", () => {
  const response = proxy(request("/welcome", true)) as WithCookies;

  expect(response.cookies.get("kairo-last-workspace")).toBeUndefined();
});
