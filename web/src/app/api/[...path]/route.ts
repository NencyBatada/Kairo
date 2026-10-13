import { identityToken } from "@/lib/core-api/identity-token";
import { SERVERLESS_AUTHORIZATION_HEADER } from "@/lib/http-names";

const HOP_BY_HOP = new Set([
  "connection",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
  "host",
  "content-length",
]);

const DECODED_BODY = new Set(["content-encoding", "content-length"]);

async function forward(request: Request): Promise<Response> {
  const baseUrl = process.env.KAIRO_API_URL;
  if (!baseUrl) {
    throw new Error("KAIRO_API_URL is not set.");
  }

  const incoming = new URL(request.url);
  const target = new URL(incoming.pathname + incoming.search, baseUrl);

  const headers = new Headers();
  request.headers.forEach((value, name) => {
    if (!HOP_BY_HOP.has(name)) {
      headers.set(name, value);
    }
  });

  const token = await identityToken();
  if (token) {
    headers.set(SERVERLESS_AUTHORIZATION_HEADER, `Bearer ${token}`);
  }

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  const answer = await fetch(target, {
    method: request.method,
    headers,
    body: hasBody ? await request.arrayBuffer() : undefined,
    redirect: "manual",
    cache: "no-store",
  });

  const responseHeaders = new Headers();
  answer.headers.forEach((value, name) => {
    if (name !== "set-cookie" && !HOP_BY_HOP.has(name) && !DECODED_BODY.has(name)) {
      responseHeaders.set(name, value);
    }
  });
  for (const cookie of answer.headers.getSetCookie()) {
    responseHeaders.append("set-cookie", cookie);
  }

  return new Response(answer.body, { status: answer.status, statusText: answer.statusText, headers: responseHeaders });
}

export const GET = forward;
export const HEAD = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;
export const OPTIONS = forward;
