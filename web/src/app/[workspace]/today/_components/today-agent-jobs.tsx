"use client";

import { AgentProgressList } from "@/components/agent-progress";
import { Card } from "@/components/ui/card";
import { useAgentJobs } from "@/components/workspace-live-provider";
import { isActive } from "@/lib/agent-progress";

export function TodayAgentJobs() {
  const jobs = useAgentJobs();
  const running = jobs.filter(isActive).length;

  return (
    <section aria-labelledby="agent-jobs-title" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h2 id="agent-jobs-title" className="font-display text-display-sm text-foreground">
          Agents at work
        </h2>
        {jobs.length > 0 && (
          <p className="text-small text-subtle-foreground">
            {running === 0 ? "Nothing running now" : running === 1 ? "1 job running" : `${running} jobs running`}
          </p>
        )}
      </div>
      {jobs.length === 0 ? (
        <p className="rounded-lg border border-border bg-card px-4 py-4 text-small text-muted-foreground md:px-5">
          No agents are working right now. Progress shows here while Kairo finds opportunities, drafts briefs and assets, checks them and
          measures results.
        </p>
      ) : (
        <Card className="gap-0 py-0">
          <AgentProgressList jobs={jobs} label="Agent jobs" />
        </Card>
      )}
    </section>
  );
}
