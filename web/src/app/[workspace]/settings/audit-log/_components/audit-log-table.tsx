import { ScrollText } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { AuditLogPage } from "@/lib/core-api/types";
import { cn } from "@/lib/utils";

const actions: Record<string, string> = {
  "brand.created": "Created the workspace",
  "brand.updated": "Changed the workspace details",
  "invitation.sent": "Sent an invitation",
  "invitation.revoked": "Revoked an invitation",
  "invitation.accepted": "Accepted an invitation",
  "invitation.declined": "Declined an invitation",
  "member.roles_changed": "Changed a member's roles",
  "member.removed": "Removed a member",
  "member.left": "Left the workspace",
  "approval_settings.changed": "Changed the approval settings",
};

type AuditLogTableProps = {
  page: AuditLogPage;
  timeZone: string;
  olderHref: (before: string) => string;
};

export function AuditLogTable({ page, timeZone, olderHref }: AuditLogTableProps) {
  const format = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });

  if (page.entries.length === 0) {
    return (
      <EmptyState
        icon={ScrollText}
        title="Nothing recorded yet"
        description="Changes to the workspace, the team and approvals will appear here, with who made them and when."
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
              <TableHead className="w-40 md:w-48">When</TableHead>
              <TableHead className="w-36 md:w-52">Who</TableHead>
              <TableHead>What</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {page.entries.map((entry) => (
              <TableRow key={String(entry.id)}>
                <TableCell className="text-small text-muted-foreground tabular-nums md:whitespace-nowrap">{format.format(new Date(entry.occurredAt))}</TableCell>
                <TableCell className="font-medium text-foreground">{entry.actorName}</TableCell>
                <TableCell className="text-foreground">{actions[entry.action] ?? entry.action}</TableCell>
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
