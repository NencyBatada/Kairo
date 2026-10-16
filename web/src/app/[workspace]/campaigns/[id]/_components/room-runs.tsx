"use client";

import { useActionState } from "react";

import { AgentProgressList } from "@/components/agent-progress";
import { FormStatus } from "@/components/form-status";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useAgentJobs } from "@/components/workspace-live-provider";
import type { components } from "@/lib/core-api/schema";
import { idle, type FormState } from "@/lib/forms";

import { dates, stageLabels } from "../_lib/presentation";

type Run = components["schemas"]["CampaignRunView"];

export function RoomRuns({ roomId, runs, retryId, retry, timeZone }: { roomId: string; runs: Run[]; retryId: string | null; retry: (previous: FormState, form: FormData) => Promise<FormState>; timeZone: string }) {
  const jobs = useAgentJobs({ kind: "campaign", id: roomId });
  const [state, action, pending] = useActionState(retry, idle);
  return <section aria-labelledby="agent-work-heading" className="min-w-0">
    <h2 id="agent-work-heading" className="mb-3 text-base font-semibold">Agent work</h2>
    {jobs.length > 0 && <div className="mb-4 rounded-lg border border-border bg-card"><AgentProgressList jobs={jobs} label="Live progress for this campaign" /></div>}
    {runs.length ? <ul className="divide-y divide-border rounded-lg border border-border bg-card">{runs.map((run) => <li key={run.id} className="p-4 md:p-5">
      <div className="flex flex-wrap items-center justify-between gap-2"><h3 className="text-sm font-semibold">{run.agent.charAt(0).toUpperCase() + run.agent.slice(1)} agent · {stageLabels[run.stage] ?? run.stage}</h3><Badge variant={run.status === "failed" ? "destructive" : run.status === "succeeded" ? "success" : run.status === "cancelled" ? "neutral" : "info"}>{({ queued: "Queued", running: "Running", succeeded: "Done", failed: "Failed", cancelled: "Cancelled" } as Record<string, string>)[run.status] ?? run.status}</Badge></div>
      <p className="mt-2 text-small text-muted-foreground">{run.status === "queued" ? "Queued" : run.finishedAt ? "Finished" : "Started"} <time dateTime={run.finishedAt ?? run.startedAt ?? run.queuedAt}>{dates(timeZone).format(new Date(run.finishedAt ?? run.startedAt ?? run.queuedAt))}</time> · {run.attempts} {Number(run.attempts) === 1 ? "attempt" : "attempts"}</p>
      {run.error && <p className="mt-2 wrap-anywhere text-small text-destructive-strong">{run.error}</p>}
      {retryId === run.id && <form action={action} className="mt-3 flex flex-wrap items-center gap-3"><Button variant="secondary" type="submit" pending={pending}>Try agent again</Button><FormStatus state={state} /></form>}
    </li>)}</ul> : <p className="rounded-lg border border-border bg-card p-4 text-small text-muted-foreground">Agent work will appear here when a stage is queued.</p>}
    {runs.length === 20 && <p className="mt-2 text-small text-muted-foreground">Showing the 20 most recent runs. Earlier actions are in the activity log.</p>}
  </section>;
}
