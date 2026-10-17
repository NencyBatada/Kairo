import Link from "next/link";
import { FlaskConical } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { date, label, lift, outcomeNames, sourceNames, stateNames } from "./_lib/model";

export const metadata: Metadata = { title: "Impact Lab" };

export default async function ImpactLabPage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params, workspace = await getWorkspace(handle);
  const list = await (await coreApi()).GET("/api/brands/{brandId}/impact-lab/analyses", { params: { path: { brandId: workspace.id } } }).then(unwrap);
  const canEdit = workspace.roles.some(role => role === "admin" || role === "marketer");
  return <div className="min-w-0 space-y-8"><PageHeader title="Impact Lab" description="Measure changes Kairo didn't launch, using a comparison the change did not touch." actions={canEdit ? <Link className={buttonVariants({ className: "h-auto min-h-11 whitespace-normal py-2" })} href={`/${handle}/impact-lab/new`}>New analysis</Link> : undefined} />
    {!canEdit && <p className="text-small text-muted-foreground">You can read saved analyses. Marketers and admins can create and run them.</p>}
    <section className="space-y-4" aria-labelledby="lab-history"><h2 id="lab-history" className="text-base font-semibold">Past analyses</h2><p className="text-small text-muted-foreground">Showing {list.analyses.length} analyses. The service returns the latest 100 analyses; this is not a total count.</p>
      {list.analyses.length ? <ul className="divide-y overflow-hidden rounded-lg border border-border bg-card">{list.analyses.map(analysis => <li key={analysis.id}><Link className="block min-w-0 space-y-3 p-4 transition-colors hover:bg-accent focus-visible:outline-offset-[-4px] md:p-5" href={`/${handle}/impact-lab/${analysis.id}`}><div className="flex flex-wrap items-center gap-2"><h3 className="font-semibold wrap-anywhere">{analysis.name}</h3><Badge variant={analysis.state === "running" ? "info" : analysis.state === "failed" ? "destructive" : "neutral"}>{stateNames[analysis.state] ?? analysis.state}</Badge>{analysis.state === "result" && <Badge variant="neutral">{outcomeNames[analysis.outcome ?? ""] ?? "Outcome unavailable"}</Badge>}</div><p className="text-small text-muted-foreground">{date(analysis.start)} to {date(analysis.end)} · {sourceNames[analysis.sourceKind] ?? analysis.sourceKind} · {label(analysis.metric)}</p>{analysis.state === "result" && <p className="text-small tabular-nums">{lift(analysis)}</p>}{analysis.memoryEntryId && <p className="text-small text-muted-foreground">Saved to Brand Memory</p>}</Link></li>)}</ul> : <EmptyState icon={FlaskConical} title="No analyses yet" description="Describe a price cut, TV burst or other change, then compare its outcome with unaffected data." />}
    </section></div>;
}
