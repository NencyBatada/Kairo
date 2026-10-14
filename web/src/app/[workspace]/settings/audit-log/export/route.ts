import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { auditActionLabel } from "../_components/audit-actions";

function csvField(value: string) {
  return `"${value.replaceAll('"', '""')}"`;
}

export async function GET(request: Request, context: RouteContext<"/[workspace]/settings/audit-log/export">) {
  const { workspace: handle } = await context.params;
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) {
    return new Response("Only admins can export the audit log.", { status: 403 });
  }

  const url = new URL(request.url);
  const action = url.searchParams.get("action") ?? undefined;
  const actor = url.searchParams.get("actor")?.trim().toLowerCase() ?? undefined;

  const page = await unwrap(
    await (await coreApi()).GET("/api/brands/{brandId}/audit-log", {
      params: { path: { brandId: workspace.id }, query: { limit: 200 } },
    }),
  );

  const details = await unwrap(
    await (await coreApi()).GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }),
  );
  const format = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "short", timeZone: details.timeZone });

  let entries = page.entries;
  if (action) {
    entries = entries.filter((entry) => entry.action === action);
  }
  if (actor) {
    entries = entries.filter((entry) => entry.actorName.toLowerCase().includes(actor));
  }

  const lines = ["When,Who,What", ...entries.map((entry) => [format.format(new Date(entry.occurredAt)), entry.actorName, auditActionLabel(entry.action)].map(csvField).join(","))];
  const body = `\uFEFF${lines.join("\n")}\n`;

  return new Response(body, {
    headers: {
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": `attachment; filename="${handle}-audit-log.csv"`,
    },
  });
}
