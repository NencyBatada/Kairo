import { Sparkles, Zap } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { outcomeLabel } from "./_lib/rules";

export const metadata: Metadata = { title: "Triggers" };

export default async function TriggersPage({ params, searchParams }: { params: Promise<{ workspace: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const raw = (await searchParams).status;
  const status = typeof raw === "string" && ["active", "paused", "draft"].includes(raw) ? raw : undefined;
  const rules = await (await coreApi()).GET("/api/brands/{brandId}/triggers/rules", { params: { path: { brandId: workspace.id }, query: { status } } }).then(unwrap);
  return <div className="flex flex-col gap-8">
    <PageHeader title="Triggers" description="Rules that turn weather, festivals and stock into opportunities ready to review." actions={workspace.roles.includes("admin") && <Link href={`/${handle}/triggers/new`} className={buttonVariants()}>Create rule</Link>} />
    <nav aria-label="Triggers" className="flex flex-wrap gap-4 text-small"><Link href={`/${handle}/triggers`} aria-current="page" className="inline-flex min-h-11 items-center border-b-2 border-primary font-semibold text-primary">Rules</Link><Link href={`/${handle}/triggers/festivals`} className="inline-flex min-h-11 items-center text-primary hover:underline">Festival calendar</Link></nav>
    <nav aria-label="Rule status" className="flex flex-wrap gap-2">{[["", "All rules"], ["active", "Active"], ["paused", "Paused"], ["draft", "Suggested"]].map(([value, label]) => <Link key={value} href={`/${handle}/triggers${value ? `?status=${value}` : ""}`} aria-current={(status ?? "") === value ? "page" : undefined} className={buttonVariants({ variant: (status ?? "") === value ? "secondary" : "ghost" })}>{label}</Link>)}</nav>
    {!workspace.roles.includes("admin") && <p className="text-small text-muted-foreground">You can review rules. A workspace admin can change them.</p>}
    {rules.length ? <ul className="divide-y divide-border rounded-lg border border-border bg-card">{rules.map((rule) => <li key={rule.id}><Link href={`/${handle}/triggers/${rule.id}`} className="flex flex-col gap-3 p-4 transition-colors hover:bg-accent focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring md:p-5">
      <div className="flex flex-wrap items-center gap-2"><h2 className="min-w-0 text-base font-semibold wrap-anywhere">{rule.name}</h2><Badge variant={rule.status === "active" ? "success" : rule.status === "paused" ? "warning" : "neutral"}>{rule.status === "draft" ? "Suggested" : rule.status === "active" ? "Active" : "Paused"}</Badge><Badge>{rule.source === "festival" ? "Festival" : rule.source === "weather" ? "Weather" : "Stock"}</Badge></div>
      <p className="max-w-prose text-small wrap-anywhere text-muted-foreground">{rule.description}</p>
      {rule.origin === "suggested" && <p className="flex items-center gap-2 text-small text-muted-foreground"><Sparkles className="size-4 shrink-0" aria-hidden="true" />Suggested by Kairo</p>}
      <p className="text-small text-muted-foreground">Fired {rule.results.fired} times · {rule.results.accepted} accepted opportunities{rule.lastCheckOutcome ? ` · ${outcomeLabel(rule.lastCheckOutcome)}` : " · Not checked yet"}</p><span className="text-small text-primary">Review rule →</span>
    </Link></li>)}</ul> : <EmptyState icon={Zap} title={status ? "No rules with this status" : "No trigger rules yet"} description="Create a rule to watch a forecast, a festival date or a stock level. It suggests an opportunity when its moment comes; campaigns still need approval." />}
  </div>;
}
