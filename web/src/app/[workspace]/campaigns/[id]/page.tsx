import { History } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { Alert } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { coreApi, unwrap } from "@/lib/core-api/client";

import { RoomRuns } from "./_components/room-runs";
import { dates } from "./_lib/presentation";
import { activityFacts } from "./_lib/evidence";
import { getRoom } from "./_lib/room";
import { retryRun } from "./actions";

export default async function CampaignRoomPage({ params, searchParams }: { params: Promise<{ workspace: string; id: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { workspace: handle, id } = await params;
  const { room, brandId, timeZone } = await getRoom(handle, id);
  const query = await searchParams;
  const before = typeof query.before === "string" && /^[1-9]\d{0,18}$/.test(query.before) && BigInt(query.before) <= BigInt("9223372036854775807") ? query.before : undefined;
  const invalid = query.before !== undefined && !before;
  const activity = await (await coreApi()).GET("/api/brands/{brandId}/campaigns/{campaignId}/activity", { params: { path: { brandId, campaignId: room.id }, query: { before } } }).then(unwrap);
  const base = `/${handle}/campaigns/${encodeURIComponent(room.id)}`;
  return <div className="grid items-start gap-8 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
    <section aria-labelledby="activity-heading" className="min-w-0">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2"><h2 id="activity-heading" className="text-base font-semibold">Activity log</h2>{before && <Link className="inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11" href={base}>Latest activity</Link>}</div>
      <p className="mb-4 text-small text-muted-foreground">Every recorded action, newest first. Entries cannot be edited or deleted.</p>
      {invalid && <Alert variant="warning" className="mb-4">That activity page link is invalid. Showing the latest entries.</Alert>}
      {activity.entries.length ? <ol className="divide-y divide-border rounded-lg border border-border bg-card">{activity.entries.map((entry) => <li key={entry.id} className="p-4 md:p-5">
        <p className="wrap-anywhere text-sm font-medium">{entry.label}</p>
        <p className="mt-1 wrap-anywhere text-small text-muted-foreground">{entry.actorName} · {entry.actorType === "agent" ? "Agent" : entry.actorType === "system" ? "System" : "Team member"}</p>
        <time dateTime={entry.occurredAt} className="mt-1 block text-xs tabular-nums text-subtle-foreground">{dates(timeZone).format(new Date(entry.occurredAt))}</time>
        {activityFacts(entry.details).length > 0 && <dl className="mt-3 space-y-2 rounded-md bg-muted p-3 text-small">{activityFacts(entry.details).map((fact) => <div key={fact.label}><dt className="text-muted-foreground">{fact.label}</dt><dd className="mt-1 whitespace-pre-wrap wrap-anywhere">{fact.value}</dd></div>)}</dl>}
      </li>)}</ol> : <EmptyState icon={History} title={before ? "No older activity" : "No activity yet"} description={before ? "Return to the latest activity to see recent actions." : "Actions by your team and Kairo will appear here as the campaign progresses."} />}
      {activity.nextBefore && <Link className={`${buttonVariants({ variant: "secondary" })} mt-4`} href={`${base}?${new URLSearchParams({ before: activity.nextBefore })}`}>Older activity</Link>}
    </section>
    <RoomRuns key={`${room.id}:${room.actions.retryRun ?? "none"}`} roomId={room.id} runs={room.runs} retryId={room.actions.retryRun} retry={retryRun.bind(null, brandId, room.id, room.actions.retryRun ?? "")} timeZone={timeZone} />
  </div>;
}
