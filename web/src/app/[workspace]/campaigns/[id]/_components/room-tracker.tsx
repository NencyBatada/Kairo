import { Check, Circle, Clock, Sparkles } from "lucide-react";
import Link from "next/link";

import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import type { components } from "@/lib/core-api/schema";

import { dates, stageLabels, stageSegment, statusLabels } from "../_lib/presentation";
import { StageTime } from "./stage-time";

export function RoomTracker({ room, handle, timeZone, now }: { room: components["schemas"]["CampaignDetail"]; handle: string; timeZone: string; now: number }) {
  const segment = stageSegment(room.stage);
  const terminal = room.stage === "closed" || room.stage === "discarded";
  const status = room.status === "needs_attention" ? "destructive" : room.status === "waiting_approval" ? "gate" : room.status === "agent_working" ? "info" : room.stage === "live" || room.stage === "results_ready" ? "success" : "neutral";
  return <section aria-label="Campaign progress" className="flex flex-col gap-5 rounded-lg border border-border bg-card p-4 md:p-5">
    <div className="flex flex-wrap items-center gap-2" role="status"><Badge variant={status}>{room.status === "waiting_approval" && <Clock aria-hidden="true" />}{statusLabels[room.status] ?? room.status}</Badge><span className="text-small font-medium">{stageLabels[room.stage] ?? room.stage}</span></div>
    <ol aria-label="Campaign stages" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {room.tracker.stages.map((step) => {
        const Icon = step.progress === "done" ? Check : step.progress === "current" ? Clock : Circle;
        return <li key={step.stage} aria-current={step.progress === "current" && !terminal ? "step" : undefined} className={`flex min-w-0 items-center gap-2 rounded-md p-2 text-small ${step.progress === "current" && !terminal ? "bg-primary-soft text-primary-strong" : "text-muted-foreground"}`}>
          <Icon className="size-4 shrink-0" aria-hidden="true" /><span className="wrap-anywhere">{step.label}<span className="sr-only"> · {step.progress === "done" ? "Completed" : step.progress === "current" ? terminal ? "Stopped here" : "Current stage" : "Upcoming"}</span></span>
        </li>;
      })}
    </ol>
    <dl className="grid gap-4 border-t border-border pt-5 sm:grid-cols-2 xl:grid-cols-4">
      <div className="min-w-0"><dt className="text-small text-muted-foreground">Campaign owner</dt><dd className="mt-1 wrap-anywhere font-medium">{room.owner.name}</dd></div>
      <div className="min-w-0"><dt className="text-small text-muted-foreground">{room.tracker.ownerKind === "agent" ? "Working now" : "Who acts next"}</dt><dd className="mt-1 flex items-start gap-2 wrap-anywhere font-medium">{room.tracker.ownerKind === "agent" && <Sparkles className="mt-1 size-4 shrink-0 text-primary" aria-hidden="true" />}{room.tracker.owner || "No action needed"}</dd></div>
      <div><dt className="text-small text-muted-foreground">{terminal ? "Final stage entered" : "Time in stage"}</dt><dd className="mt-1 text-small tabular-nums"><time dateTime={room.tracker.enteredAt} title={dates(timeZone).format(new Date(room.tracker.enteredAt))}>{terminal ? dates(timeZone).format(new Date(room.tracker.enteredAt)) : <StageTime key={room.tracker.enteredAt} enteredAt={room.tracker.enteredAt} now={now} />}</time></dd></div>
      <div className="min-w-0"><dt className="text-small text-muted-foreground">Next action</dt><dd className="mt-1 text-small wrap-anywhere">{room.tracker.nextAction}</dd></div>
    </dl>
    {room.pendingApproval && <Alert><p className="font-medium">{room.pendingApproval.label} · Waiting for approval</p><p className="mt-1 wrap-anywhere">{room.pendingApproval.approvers.length ? `Approvers: ${room.pendingApproval.approvers.map((person) => person.name).join(", ")}.` : "No approver is assigned. Ask a workspace admin to check approval settings."}</p></Alert>}
    {room.lastSendBack && <Alert variant="warning"><p className="font-medium">Sent back by {room.lastSendBack.by.name}</p><p className="mt-1 wrap-anywhere">{room.lastSendBack.comment}</p><p className="mt-2 text-small">{stageLabels[room.lastSendBack.from] ?? room.lastSendBack.from} to {stageLabels[room.lastSendBack.to] ?? room.lastSendBack.to} · {dates(timeZone).format(new Date(room.lastSendBack.at))}</p></Alert>}
    {room.discardReason && <Alert><p className="font-medium">Campaign discarded</p><p className="mt-1 wrap-anywhere">{room.discardReason}</p></Alert>}
    {!terminal && segment && <div><Link className={buttonVariants({ variant: "secondary" })} href={`/${handle}/campaigns/${encodeURIComponent(room.id)}/${segment}`}>Open {segment}</Link></div>}
  </section>;
}
