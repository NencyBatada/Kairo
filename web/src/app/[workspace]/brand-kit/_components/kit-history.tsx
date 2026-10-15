import Link from "next/link";

import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

import type { KitHistory } from "./types";

export function KitHistoryList({ versions, handle, timeZone, names = {} }: { versions: KitHistory[]; handle: string; timeZone: string; names?: Record<string, string> }) {
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });
  return <Card><CardHeader><CardTitle>Version history</CardTitle></CardHeader><CardContent>
    {!versions.length && <p className="text-small text-muted-foreground">Saved versions will appear here. Every change needs approval before agents use it.</p>}
    <ul className="divide-y divide-border">{versions.map((version) => <li key={version.number}>
      <Link href={`/${handle}/brand-kit/versions/${version.number}`} className="flex min-w-0 flex-col gap-2 rounded-md px-2 py-4 hover:bg-accent sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0"><p className="text-sm font-semibold">Version {version.number}{version.restoredFrom !== null ? ` · Restored from version ${version.restoredFrom}` : ""}</p><p className="text-small wrap-anywhere text-muted-foreground">Updated by {names[version.updatedBy] ?? version.updatedBy} · {dates.format(new Date(version.updatedAt))}</p>{version.approvedAt && <p className="text-small wrap-anywhere text-muted-foreground">Approved by {names[version.approvedBy ?? ""] ?? version.approvedBy} · {dates.format(new Date(version.approvedAt))}</p>}</div>
        <Badge variant={version.status === "approved" ? "success" : "neutral"}>{version.status.charAt(0).toUpperCase() + version.status.slice(1)}</Badge>
      </Link>
    </li>)}</ul>
  </CardContent></Card>;
}
