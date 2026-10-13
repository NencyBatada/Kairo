import { cookies } from "next/headers";

import { coreApi } from "@/lib/core-api/client";
import { LAST_WORKSPACE_COOKIE } from "@/lib/http-names";

export async function GET() {
  const { data, response } = await (await coreApi()).GET("/api/brands");
  if (response.status === 401 || !data) {
    return redirectTo("/sign-in");
  }

  const last = (await cookies()).get(LAST_WORKSPACE_COOKIE)?.value;
  const target = data.find((workspace) => workspace.handle === last) ?? data[0];
  return redirectTo(target ? `/${target.handle}/today` : "/welcome");
}

function redirectTo(location: string) {
  return new Response(null, { status: 307, headers: { Location: location } });
}
