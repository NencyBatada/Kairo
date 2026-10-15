import "server-only";

import { cookies } from "next/headers";

import { signInAgain } from "@/lib/core-api/client";
import { identityToken } from "@/lib/core-api/identity-token";
import { REQUEST_HEADER, SERVERLESS_AUTHORIZATION_HEADER, SESSION_COOKIE } from "@/lib/http-names";

export async function catalogueRequest(brandId: string, path: string, options: RequestInit = {}) {
  const baseUrl = process.env.KAIRO_API_URL;
  if (!baseUrl) throw new Error("KAIRO_API_URL is not set.");
  const session = (await cookies()).get(SESSION_COOKIE);
  const token = await identityToken();
  const headers = new Headers(options.headers);
  headers.set(REQUEST_HEADER, "1");
  if (session) headers.set("Cookie", `${SESSION_COOKIE}=${session.value}`);
  if (token) headers.set(SERVERLESS_AUTHORIZATION_HEADER, `Bearer ${token}`);
  if (options.body && typeof options.body === "string") headers.set("Content-Type", "application/json");
  return fetch(`${baseUrl.replace(/\/$/, "")}/api/brands/${encodeURIComponent(brandId)}/catalogue${path}`, { ...options, headers, cache: "no-store" });
}

export async function catalogueJson<T>(response: Response): Promise<T> {
  if (response.status === 401) return signInAgain();
  if (!response.ok) throw new Error(`The catalogue API answered ${response.status}.`);
  return response.json() as Promise<T>;
}
