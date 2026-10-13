import Link from "next/link";

import type { AuditLogPage } from "@/lib/core-api/types";

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
    return <p className="text-sm text-zinc-600 dark:text-zinc-400">Nothing has been recorded yet.</p>;
  }

  return (
    <div className="space-y-4">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-black/10 dark:border-white/15">
            <th className="py-2 font-medium">When</th>
            <th className="py-2 font-medium">Who</th>
            <th className="py-2 font-medium">What</th>
          </tr>
        </thead>
        <tbody>
          {page.entries.map((entry) => (
            <tr key={String(entry.id)} className="border-b border-black/5 dark:border-white/10">
              <td className="py-2">{format.format(new Date(entry.occurredAt))}</td>
              <td className="py-2">{entry.actorName}</td>
              <td className="py-2">{actions[entry.action] ?? entry.action}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {page.nextBefore && (
        <Link href={olderHref(page.nextBefore)} className="text-sm underline-offset-2 hover:underline">
          Older entries
        </Link>
      )}
    </div>
  );
}
