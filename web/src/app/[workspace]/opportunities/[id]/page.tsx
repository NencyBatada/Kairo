import { ExternalLink, Eye, Sparkles } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { growth, number, webUrl } from "@/components/opportunities/format";
import { OpportunityProducts } from "@/components/opportunities/products";
import { OpportunityTrend } from "@/components/opportunities/trend";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { OpportunityDecisions } from "./_components/opportunity-decisions";
import { acceptOpportunity, dismissOpportunity, saveOpportunity, unsaveOpportunity } from "./actions";

export const metadata: Metadata = { title: "Opportunity" };

export default async function OpportunityPage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const [item, details] = await Promise.all([
    api.GET("/api/brands/{brandId}/opportunities/{opportunityId}", { params: { path: { brandId: workspace.id, opportunityId: id } } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }).then(unwrap),
  ]);
  const breakdown = item.scoreBreakdown;
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: details.timeZone });
  const factors = [
    { name: "Growth", score: breakdown.growthScore, weight: breakdown.growthWeight, detail: growth(breakdown.growth) },
    { name: "Search volume", score: breakdown.volumeScore, weight: breakdown.volumeWeight, detail: `${number(breakdown.lastMonthSearches)} searches last month` },
    { name: "Product fit", score: breakdown.fitScore, weight: breakdown.fitWeight, detail: breakdown.fitReason ?? "No product-fit explanation is available." },
  ];
  return <div className="flex flex-col gap-8">
    <PageHeader eyebrow="Opportunity Radar" title={item.topic} description="The demand, products and evidence behind this opportunity." actions={<Link href={`/${handle}/opportunities`} className="text-small text-primary hover:underline">All opportunities</Link>} />
    <div className="flex flex-wrap gap-2"><Badge variant={item.status === "accepted" ? "success" : "neutral"}>{item.status.charAt(0).toUpperCase() + item.status.slice(1)}</Badge>{item.watched && <Badge><Eye className="size-3" aria-hidden="true" />Watched</Badge>}<p className="text-small text-muted-foreground">Last seen {dates.format(new Date(item.lastSeenAt))}</p></div>
    <dl className="grid grid-cols-2 gap-4 xl:grid-cols-4">{[["Score / 100", number(item.score)], ["Month-on-month growth", growth(item.mom)], ["Year-on-year growth", growth(item.yoy)], ["Last month’s searches", number(item.lastMonthSearches)]].map(([label, value]) => <div key={label} className="min-w-0 rounded-lg border border-border bg-card p-4"><dt className="text-small text-muted-foreground">{label}</dt><dd className="mt-2 font-display text-display-md tabular-nums">{value}</dd></div>)}</dl>
    <OpportunityDecisions key={`${item.status}:${item.available}`} status={item.status} available={item.available} canDecide={workspace.roles.includes("marketer")} roomHref={item.roomId ? `/${handle}/campaigns/${item.roomId}` : undefined} accept={acceptOpportunity.bind(null, workspace.id, id, handle)} save={saveOpportunity.bind(null, workspace.id, id)} unsave={unsaveOpportunity.bind(null, workspace.id, id)} dismiss={dismissOpportunity.bind(null, workspace.id, id)} />
    <div className="grid items-start gap-5 xl:grid-cols-2">
      <Card><CardHeader><CardTitle>Search trend</CardTitle></CardHeader><CardContent><OpportunityTrend points={item.trend} />{item.trend.length < 12 && <p className="mt-3 text-small text-warning-strong">Less than 12 months of search history is available. Treat comparisons with care.</p>}</CardContent></Card>
      <Card><CardHeader><CardTitle>Score breakdown</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">{factors.map((factor) => <div key={factor.name} className="text-small">
        <div className="mb-2 flex flex-wrap justify-between gap-2"><h3 className="font-semibold">{factor.name}</h3><span className="tabular-nums">{number(factor.score)} / 100 · {number(Number(factor.weight) * 100)}% weight</span></div>
        <meter min={0} max={100} value={Number(factor.score)} aria-label={`${factor.name} score`} className="h-3 w-full accent-primary">{number(factor.score)}</meter><p className="mt-2 wrap-anywhere text-muted-foreground">{factor.detail}</p>
      </div>)}<div className="rounded-md bg-muted p-3 text-small"><p className="font-medium">Feedback adjustment ×{Number(breakdown.feedback).toFixed(2)}</p><p className="mt-1 text-muted-foreground">{breakdown.feedbackReasons.length ? `Earlier dismissals affected this score: ${breakdown.feedbackReasons.map((reason) => reason.replaceAll("_", " ")).join(", ")}.` : "No adjustment from earlier dismissals."}</p><p className="mt-2 tabular-nums">Final score: {number(breakdown.total)} / 100</p></div></CardContent></Card>
      <Card><CardHeader><CardTitle>Regional demand</CardTitle></CardHeader><CardContent>{item.regions.length ? <table className="w-full text-small"><caption className="sr-only">Search demand by region</caption><thead className="bg-muted text-xs text-subtle-foreground"><tr><th scope="col" className="p-2 text-left">Region</th><th scope="col" className="p-2 text-right">Searches</th><th scope="col" className="p-2 text-right">Growth</th></tr></thead><tbody>{item.regions.map((region) => <tr key={region.id} className="border-b border-border"><th scope="row" className="p-2 text-left font-normal wrap-anywhere">{region.name}{region.rising && <span className="block text-xs text-muted-foreground">Rising</span>}</th><td className="p-2 text-right tabular-nums">{number(region.lastMonthSearches)}</td><td className="p-2 text-right tabular-nums">{growth(region.growth)}</td></tr>)}</tbody></table> : <p className="text-small text-muted-foreground">No regional demand is available yet.</p>}</CardContent></Card>
      <Card><CardHeader><CardTitle>Matching products in stock</CardTitle></CardHeader><CardContent><OpportunityProducts products={item.products} /></CardContent></Card>
    </div>
    <Card><CardHeader><CardTitle>Why demand is rising</CardTitle></CardHeader><CardContent className="flex flex-col gap-4">
      {item.reasons.length > 0 && <p className="flex items-center gap-2 text-small text-muted-foreground"><Sparkles className="size-4" aria-hidden="true" />Drafted by Kairo</p>}
      {!item.reasons.length && <Alert variant="warning">No source currently supports an explanation for this opportunity. Use the search data and product matches to assess it.</Alert>}
      {item.reasons.map((reason, index) => {
        const sources = reason.sources.filter((source) => webUrl(source.url));
        return <div key={index} className="rounded-md bg-muted p-4"><h3 className="mb-2 text-sm font-semibold">Why</h3><p className="max-w-prose wrap-anywhere">{reason.text}</p><div className="mt-3 flex flex-wrap gap-2">{sources.map((source, sourceIndex) => <a key={sourceIndex} href={webUrl(source.url)} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-8 max-w-full items-center gap-2 rounded-full border border-border-strong px-3 py-1 text-small text-primary hover:bg-accent pointer-coarse:min-h-11"><span className="min-w-0 wrap-anywhere">{source.title}</span><ExternalLink className="size-3 shrink-0" aria-hidden="true" /><span className="sr-only">Opens in a new tab</span></a>)}</div>{!sources.length && <p className="mt-2 text-small text-warning-strong">No source supports this reason.</p>}</div>;
      })}
      {item.reasonsStatus !== "sourced" && item.reasons.length > 0 && <p className="text-small text-warning-strong">Source status: {item.reasonsStatus.replaceAll("_", " ")}. Check the linked evidence before acting.</p>}
    </CardContent></Card>
    {item.dismissReason && <Alert>This was dismissed as {item.dismissReason.replaceAll("_", " ")}.{item.dismissNote && <p className="mt-1 wrap-anywhere">{item.dismissNote}</p>}</Alert>}
  </div>;
}
