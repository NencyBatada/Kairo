import { Sparkles } from "lucide-react";
import Link from "next/link";
import { Alert } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { components } from "@/lib/core-api/schema";
import { TriggerSummary } from "./trigger-summary";

export function TriggerEvidence({ triggers, brief, briefStatus, handle, timeZone }: { triggers: components["schemas"]["TriggerView"][]; brief: components["schemas"]["BriefView"] | null; briefStatus: string | null; handle: string; timeZone: string }) {
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });
  return <section aria-labelledby="trigger-evidence-title" className="flex flex-col gap-5">
    <h2 id="trigger-evidence-title" className="text-base font-semibold">What triggered this opportunity</h2>
    <ul className="grid gap-4 xl:grid-cols-2">{triggers.map((trigger) => <li key={trigger.firingId} className="flex flex-col gap-3 rounded-lg border border-border bg-card p-4"><TriggerSummary trigger={trigger} /><p className="text-small text-muted-foreground">Fired {dates.format(new Date(trigger.firedAt))} · {trigger.ended ? "Condition ended" : `Condition ends ${dates.format(new Date(trigger.conditionEndsAt))}`}</p><Link href={`/${handle}/triggers/${trigger.ruleId}`} className="inline-flex min-h-8 items-center text-small text-primary hover:underline pointer-coarse:min-h-11">View rule: {trigger.ruleName}</Link></li>)}</ul>
    <Card><CardHeader><CardTitle level={3}>Suggested brief</CardTitle></CardHeader><CardContent className="flex flex-col gap-4">
      {brief ? <><p className="flex items-center gap-2 text-small text-muted-foreground"><Sparkles className="size-4" aria-hidden="true" />Drafted by Kairo · {dates.format(new Date(brief.draftedAt))}</p><h4 className="font-semibold wrap-anywhere">{brief.title}</h4><dl className="grid gap-4 md:grid-cols-2">{[["Goal", brief.goal], ["Audience", brief.audience], ["Key message", brief.keyMessage], ["Offer", brief.offer ?? "No offer suggested."], ["Regions", brief.regions.join(", ") || "No regions specified."], ["Channels", brief.channels.join(", ") || "No channels specified."], ["Timing", brief.timing]].map(([label, value]) => <div key={label} className="min-w-0"><dt className="text-small font-medium">{label}</dt><dd className="mt-1 text-small wrap-anywhere text-muted-foreground">{value}</dd></div>)}</dl></> : <Alert variant={briefStatus === "failed" ? "warning" : "info"}>{briefStatus === "failed" ? "The brief could not be drafted. Review the trigger and matching products; a full brief can be drafted after you accept the opportunity." : "The suggested brief is not available yet. The trigger and matching products are ready to review."}</Alert>}
      <p className="text-small text-muted-foreground">Accepting opens a campaign room. The brief, assets and launch still need approval before any money is spent.</p>
    </CardContent></Card>
  </section>;
}
