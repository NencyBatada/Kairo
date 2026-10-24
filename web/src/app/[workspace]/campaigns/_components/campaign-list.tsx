import { ArrowRight, Clock } from "lucide-react";
import Link from "next/link";

import { Badge } from "@/components/ui/badge";
import type { components } from "@/lib/core-api/schema";

import { stages, statuses } from "../_lib/filters";

export function CampaignList({ campaigns, base, timeZone }: { campaigns: components["schemas"]["CampaignCard"][]; base: string; timeZone: string }) {
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });
  return <ul aria-label="Campaign rooms" className="divide-y divide-border overflow-hidden rounded-lg border border-border bg-card">{campaigns.map((campaign) => {
    const variant = campaign.status === "needs_attention" ? "destructive" : campaign.status === "waiting_approval" ? "gate" : campaign.status === "agent_working" ? "info" : campaign.stage === "live" || campaign.stage === "results_ready" ? "success" : "neutral";
    return <li key={campaign.id}><Link href={`${base}/${encodeURIComponent(campaign.id)}`} className="group flex items-start gap-4 p-4 transition-colors duration-120 hover:bg-accent focus-visible:bg-accent focus-visible:ring-inset md:p-5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-col items-start gap-2 sm:flex-row"><h2 className="min-w-0 w-full font-display text-display-sm wrap-anywhere sm:w-auto sm:flex-1">{campaign.title}</h2><Badge variant={variant}>{campaign.status === "waiting_approval" && <Clock aria-hidden="true" />}{statuses[campaign.status] ?? campaign.status}</Badge></div>
        <p className="mt-2 text-small text-muted-foreground wrap-anywhere">{campaign.products.length ? campaign.products.map((product) => product.title).join(" · ") : "No product names are available."}</p>
        <dl className="mt-4 grid gap-3 text-small sm:grid-cols-2 xl:grid-cols-4">
          <div className="min-w-0"><dt className="text-muted-foreground">Stage</dt><dd className="mt-1 wrap-anywhere">{stages[campaign.stage] ?? campaign.stage}</dd></div>
          <div className="min-w-0"><dt className="text-muted-foreground">Owner</dt><dd className="mt-1 wrap-anywhere">{campaign.owner.name}</dd></div>
          <div className="min-w-0"><dt className="text-muted-foreground">Opened</dt><dd className="mt-1 tabular-nums"><time dateTime={campaign.openedAt}>{dates.format(new Date(campaign.openedAt))}</time></dd></div>
          <div className="min-w-0"><dt className="text-muted-foreground">Last updated</dt><dd className="mt-1 tabular-nums"><time dateTime={campaign.updatedAt}>{dates.format(new Date(campaign.updatedAt))}</time></dd></div>
        </dl>
        {campaign.pendingGate && <p className="mt-3 flex items-center gap-2 text-small text-primary-strong"><Clock className="size-4 shrink-0" aria-hidden="true" />{({ brief: "Gate 1 · Brief", assets: "Gate 2 · Assets", launch: "Gate 3 · Launch" } as Record<string, string>)[campaign.pendingGate] ?? "Approval"} waiting</p>}
      </div><ArrowRight className="mt-1 size-4 shrink-0 text-muted-foreground transition-transform duration-120 motion-safe:group-hover:translate-x-0.5" aria-hidden="true" />
    </Link></li>;
  })}</ul>;
}
