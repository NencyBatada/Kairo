import type { Metadata } from "next";

import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { AuditLogTable } from "./_components/audit-log-table";

export const metadata: Metadata = { title: "Audit log" };

export default async function AuditLogPage({ params, searchParams }: PageProps<"/[workspace]/settings/audit-log">) {
  const [{ workspace: handle }, { before }] = await Promise.all([params, searchParams]);
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

  return (
    <AuditLogTable
      page={page}
      timeZone={details.timeZone}
      olderHref={(cursor) => `/${handle}/settings/audit-log?before=${encodeURIComponent(cursor)}`}
    />
  );
}
