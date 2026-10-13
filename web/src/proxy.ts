import { NextResponse, type NextRequest } from "next/server";

import { LAST_WORKSPACE_COOKIE, PATH_HEADER, SESSION_COOKIE } from "@/lib/http-names";

const TOP_LEVEL_ROUTES = new Set(["sign-in", "welcome", "health"]);

export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const path = pathname + search;

  if (pathname !== "/sign-in" && !request.cookies.has(SESSION_COOKIE)) {
    return new Response(null, {
      status: 307,
      headers: { Location: `/sign-in?returnUrl=${encodeURIComponent(path)}` },
    });
  }

  const headers = new Headers(request.headers);
  headers.set(PATH_HEADER, path);
  const response = NextResponse.next({ request: { headers } });

  const handle = pathname.split("/")[1];
  if (handle && !TOP_LEVEL_ROUTES.has(handle)) {
    response.cookies.set(LAST_WORKSPACE_COOKIE, handle, {
      path: "/",
      httpOnly: true,
      sameSite: "lax",
      maxAge: 60 * 60 * 24 * 365,
    });
  }

  return response;
}

export const config = {
  matcher: ["/((?!api|_next|favicon.ico|robots.txt|health).*)"],
};
