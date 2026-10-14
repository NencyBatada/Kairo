import { ScrollText } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { AuditLogPage } from "@/lib/core-api/types";
import { cn } from "@/lib/utils";

import { actorTypeLabels } from "./audit-log-search";

type AuditLogTableProps = {
  page: AuditLogPage;
  timeZone: string;
  actionLabels: Record<string, string>;
  olderHref: (before: string) => string;
  filtered?: boolean;
};

const targetLabels: Record<string, string> = {
  brand: "Workspace",
  brand_kit_version: "Brand Kit version",
  brand_kit_file: "Brand Kit file",
  connection: "Connection",
  invitation: "Invitation",
  member: "Member",
  sales_source: "Sales source",
  sales_upload: "Sales upload",
  user: "Person",
};

function targetLabel(targetType: string) {
  return targetLabels[targetType] ?? targetType.replaceAll("_", " ");
}

export function AuditLogTable({ page, timeZone, actionLabels, olderHref, filtered = false }: AuditLogTableProps) {
  const format = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });

  if (page.entries.length === 0) {
    return (
      <EmptyState
        icon={ScrollText}
        title={filtered ? "No matching entries" : "Nothing recorded yet"}
        description={
          filtered
            ? "Nothing matches your filters. Try widening the dates or clearing the filters."
            : "Changes to the workspace, the team and approvals will appear here, with who made them and when."
        }
      />
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <Card className="gap-0 overflow-hidden py-0 md:gap-0 md:py-0">
        <Table>
          <TableCaption>Audit log, newest first</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead className="md:w-44">When</TableHead>
              <TableHead className="md:w-56">Who</TableHead>
              <TableHead>What</TableHead>
              <TableHead className="md:w-56">Target</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {page.entries.map((entry) => (
              <TableRow key={String(entry.id)}>
                <TableCell className="text-small text-muted-foreground tabular-nums md:whitespace-nowrap">
                  <time dateTime={entry.occurredAt}>{format.format(new Date(entry.occurredAt))}</time>
                </TableCell>
                <TableCell>
                  <div className="flex min-w-0 flex-col gap-1">
                    <span className="truncate font-medium text-foreground" title={entry.actorEmail ?? entry.actorName}>
                      {entry.actorName}
                    </span>
                    <Badge variant={entry.actorType === "agent" ? "info" : "neutral"}>{actorTypeLabels[entry.actorType]}</Badge>
                  </div>
                </TableCell>
                <TableCell className="text-foreground">{actionLabels[entry.action] ?? entry.action}</TableCell>
                <TableCell>
                  <div className="flex min-w-0 flex-col gap-0.5">
                    <span className="text-small text-foreground">{targetLabel(entry.targetType)}</span>
                    <span className="truncate font-mono text-xs text-subtle-foreground" title={entry.targetId}>
                      {entry.targetId}
                    </span>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
      {page.nextBefore && (
        <Link href={olderHref(page.nextBefore)} className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "self-start")}>
          Older entries
        </Link>
      )}
    </div>
  );
}
