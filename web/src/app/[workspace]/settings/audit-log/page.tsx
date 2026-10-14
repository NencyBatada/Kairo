import type { Metadata } from "next";

import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { AuditLogFilters } from "./_components/audit-log-filters";
import { AuditLogTable } from "./_components/audit-log-table";

export const metadata: Metadata = { title: "Audit log" };

export default async function AuditLogPage({ params, searchParams }: PageProps<"/[workspace]/settings/audit-log">) {
  const [{ workspace: handle }, query] = await Promise.all([params, searchParams]);
  const { before, action, actor } = query;
  const actionFilter = typeof action === "string" && action !== "" ? action : undefined;
  const actorFilter = typeof actor === "string" && actor.trim() !== "" ? actor.trim() : undefined;
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) {
    return <Alert className="max-w-160">Only admins can see the audit log.</Alert>;
  }

  const api = await coreApi();
  const brandId = workspace.id;
  const [page, details] = await Promise.all([
    unwrap(await api.GET("/api/brands/{brandId}/audit-log", {
      params: { path: { brandId }, query: { before: typeof before === "string" ? before : undefined } },
    })),
    unwrap(await api.GET("/api/brands/{brandId}", { params: { path: { brandId } } })),
  ]);

  let entries = page.entries;
  if (actionFilter) {
    entries = entries.filter((entry) => entry.action === actionFilter);
  }
  if (actorFilter) {
    const needle = actorFilter.toLowerCase();
    entries = entries.filter((entry) => entry.actorName.toLowerCase().includes(needle));
  }

  const filtered = Boolean(actionFilter || actorFilter);
  const querySuffix = [
    actionFilter ? `action=${encodeURIComponent(actionFilter)}` : "",
    actorFilter ? `actor=${encodeURIComponent(actorFilter)}` : "",
  ]
    .filter(Boolean)
    .join("&");

  return (
    <div className="flex flex-col gap-6">
      <AuditLogFilters
        workspace={handle}
        action={actionFilter}
        actor={actorFilter}
        before={typeof before === "string" ? before : undefined}
      />
      <AuditLogTable
        page={{ ...page, entries }}
        timeZone={details.timeZone}
        filtered={filtered}
        olderHref={(cursor) => {
          const parts = [`before=${encodeURIComponent(cursor)}`, querySuffix].filter(Boolean);
          return `/${handle}/settings/audit-log?${parts.join("&")}`;
        }}
      />
    </div>
  );
}
