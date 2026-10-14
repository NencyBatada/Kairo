import { ScrollText } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { AuditLogPage } from "@/lib/core-api/types";
import { cn } from "@/lib/utils";

import { auditActionLabel } from "./audit-actions";

type AuditLogTableProps = {
  page: AuditLogPage;
  timeZone: string;
  olderHref: (before: string) => string;
  filtered?: boolean;
};

export function AuditLogTable({ page, timeZone, olderHref, filtered = false }: AuditLogTableProps) {
  const format = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });

  if (page.entries.length === 0) {
    return (
      <EmptyState
        icon={ScrollText}
        title={filtered ? "No matching entries" : "Nothing recorded yet"}
        description={
          filtered
            ? "Nothing on this page matches your filters. Try clearing them or loading older entries."
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
              <TableHead className="md:w-48">When</TableHead>
              <TableHead className="md:w-52">Who</TableHead>
              <TableHead>What</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {page.entries.map((entry) => (
              <TableRow key={String(entry.id)}>
                <TableCell className="text-small text-muted-foreground tabular-nums md:whitespace-nowrap">{format.format(new Date(entry.occurredAt))}</TableCell>
                <TableCell className="font-medium text-foreground">{entry.actorName}</TableCell>
                <TableCell className="text-foreground">{auditActionLabel(entry.action)}</TableCell>
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
