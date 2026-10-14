import { Sparkles } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { type AgentJobProgress, type AgentJobStatus, agentLabel } from "@/lib/agent-progress";
import { cn } from "@/lib/utils";

const statusBadges: Record<AgentJobStatus, { label: string; variant: "info" | "success" | "destructive" }> = {
  queued: { label: "Queued", variant: "info" },
  running: { label: "Running", variant: "info" },
  succeeded: { label: "Done", variant: "success" },
  failed: { label: "Failed", variant: "destructive" },
};

function stepText(job: AgentJobProgress) {
  return job.stepNumber !== null && job.stepCount !== null ? `Step ${job.stepNumber} of ${job.stepCount}` : null;
}

export function AgentProgress({ job, className }: { job: AgentJobProgress; className?: string }) {
  const badge = statusBadges[job.status];
  const steps = stepText(job);
  const fraction = job.stepNumber !== null && job.stepCount ? job.stepNumber / job.stepCount : null;
  const name = `${agentLabel(job.agent)} agent`;

  return (
    <div className={cn("flex flex-col gap-2", className)} data-job-id={job.jobId}>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <Sparkles className="size-4 shrink-0 text-primary" aria-hidden="true" />
        <span className="font-semibold text-foreground">{name}</span>
        <Badge variant={badge.variant}>{badge.label}</Badge>
      </div>
      <p className="text-small text-foreground wrap-anywhere">
        {job.step}
        {steps && <span className="text-subtle-foreground"> · {steps}</span>}
      </p>
      {fraction !== null && (
        <div
          role="progressbar"
          aria-label={`${name} progress`}
          aria-valuemin={0}
          aria-valuemax={job.stepCount ?? undefined}
          aria-valuenow={job.stepNumber ?? undefined}
          aria-valuetext={steps ?? undefined}
          className="h-1.5 w-full overflow-hidden rounded-full bg-accent"
        >
          <div
            className={cn(
              "h-full w-full origin-left rounded-full transition-transform duration-200 ease-out motion-reduce:transition-none",
              job.status === "failed" ? "bg-destructive" : job.status === "succeeded" ? "bg-success" : "bg-info",
            )}
            style={{ transform: `scaleX(${fraction})` }}
          />
        </div>
      )}
      {job.message && <p className="max-w-prose text-small text-muted-foreground wrap-anywhere">{job.message}</p>}
    </div>
  );
}

export function AgentProgressList({ jobs, label, className }: { jobs: AgentJobProgress[]; label: string; className?: string }) {
  return (
    <ul aria-label={label} aria-live="polite" className={cn("divide-y divide-border", className)}>
      {jobs.map((job) => (
        <li key={job.jobId} className="px-4 py-4 md:px-5">
          <AgentProgress job={job} />
        </li>
      ))}
    </ul>
  );
}
