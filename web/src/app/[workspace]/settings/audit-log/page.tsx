import type { Metadata } from "next";

import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { AuditLogFilters } from "./_components/audit-log-filters";
import { auditLogQuery, coreAuditLogQuery, hasFilters, parseAuditLogSearch } from "./_components/audit-log-search";
import { AuditLogTable } from "./_components/audit-log-table";

export const metadata: Metadata = { title: "Audit log" };

function problemMessages(error: unknown) {
  const errors = (error as { errors?: Record<string, string[]> } | undefined)?.errors ?? {};
  const messages = Object.values(errors).flat();
  return messages.length > 0 ? messages : ["Those filters can't be used. Check them and try again."];
}

export default async function AuditLogPage({ params, searchParams }: PageProps<"/[workspace]/settings/audit-log">) {
  const [{ workspace: handle }, query] = await Promise.all([params, searchParams]);
  const { search, before } = parseAuditLogSearch(query);
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) {
    return <Alert className="max-w-160">Only admins can see the audit log.</Alert>;
  }

  const api = await coreApi();
  const brandId = workspace.id;
  const [result, actions, details] = await Promise.all([
    api.GET("/api/brands/{brandId}/audit-log", {
      params: { path: { brandId }, query: { ...coreAuditLogQuery(search), before } },
    }),
    api.GET("/api/brands/{brandId}/audit-log/actions", { params: { path: { brandId } } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: { path: { brandId } } }).then(unwrap),
  ]);
  const actionLabels = Object.fromEntries(actions.map((option) => [option.action, option.label]));
  const filters = <AuditLogFilters workspace={handle} brandId={brandId} actions={actions} search={search} />;

  if (result.response.status === 400) {
    return (
      <div className="flex flex-col gap-6">
        {filters}
        <Alert variant="destructive">{problemMessages(result.error).join(" ")}</Alert>
      </div>
    );
  }

  const page = await unwrap(result);

  return (
    <div className="flex flex-col gap-6">
      {filters}
      <AuditLogTable
        page={page}
        timeZone={details.timeZone}
        actionLabels={actionLabels}
        filtered={hasFilters(search)}
        olderHref={(cursor) => `/${handle}/settings/audit-log${auditLogQuery(search, cursor)}`}
      />
    </div>
  );
}
