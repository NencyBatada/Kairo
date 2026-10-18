import "server-only";

import { cookies, headers } from "next/headers";
import { notFound, redirect } from "next/navigation";
import createClient from "openapi-fetch";

import { identityToken } from "./identity-token";
import { traceHeaders } from "@/lib/trace-context";
import type { paths } from "./schema";

import { PATH_HEADER, REQUEST_HEADER, SERVERLESS_AUTHORIZATION_HEADER, SESSION_COOKIE } from "@/lib/http-names";

export async function coreApi() {
  const session = (await cookies()).get(SESSION_COOKIE);
  const baseUrl = process.env.KAIRO_API_URL;
  if (!baseUrl) {
    throw new Error("KAIRO_API_URL is not set.");
  }

  const token = await identityToken();

  return createClient<paths>({
    baseUrl,
    headers: {
      ...traceHeaders(),
      [REQUEST_HEADER]: "1",
      ...(token ? { [SERVERLESS_AUTHORIZATION_HEADER]: `Bearer ${token}` } : {}),
      ...(session ? { cookie: `${SESSION_COOKIE}=${session.value}` } : {}),
    },
  });
}

export async function signInAgain(): Promise<never> {
  const path = (await headers()).get(PATH_HEADER) ?? "/";
  redirect(`/sign-in?returnUrl=${encodeURIComponent(path)}`);
}

export async function unwrap<T>(result: { data?: T; response: Response }): Promise<T> {
  if (result.response.status === 401) {
    return signInAgain();
  }

  if (result.response.status === 404) {
    notFound();
  }

  if (result.data === undefined) {
    throw new Error(`The core API answered ${result.response.status}.`);
  }

  return result.data;
}
