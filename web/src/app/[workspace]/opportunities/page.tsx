import { TrendingUp } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { OpportunityCard } from "./_components/opportunity-card";
import { opportunityFilters, statuses } from "./_lib/filters";
import { refreshMessage } from "./_lib/refresh-message";

export const metadata: Metadata = { title: "Opportunities" };

export default async function OpportunitiesPage({ params, searchParams }: { params: Promise<{ workspace: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const filters = opportunityFilters(await searchParams);
  const api = await coreApi();
  const [list, details] = await Promise.all([
    api.GET("/api/brands/{brandId}/opportunities", { params: { path: { brandId: workspace.id }, query: filters } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }).then(unwrap),
  ]);
  const filtered = filters.status !== "open" || !!filters.category || filters.watched || filters.minScore !== undefined;
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: details.timeZone });
  return <div className="flex flex-col gap-8">
    <PageHeader title="Opportunities" description="Rising demand for your products, ranked by growth, search volume and product fit." actions={<Link href={`/${handle}/opportunities/topics`} className={buttonVariants({ variant: "secondary" })}>Manage topics</Link>} />
    <div className="flex flex-wrap items-center justify-between gap-3 text-small text-muted-foreground">
      <p>{list.refreshedAt ? `Last refreshed ${dates.format(new Date(list.refreshedAt))}` : "The first opportunity refresh has not completed yet."}</p>
      <Link href={`/${handle}/opportunities?status=saved`} className="inline-flex min-h-6 items-center text-primary hover:underline pointer-coarse:min-h-11">Saved opportunities</Link>
    </div>
    {list.lastOutcome && list.lastOutcome !== "succeeded" && <Alert variant="warning">{refreshMessage(list.lastReason)}</Alert>}
    <form action={`/${handle}/opportunities`} method="get" className="grid gap-4 rounded-lg border border-border bg-card p-4 md:grid-cols-2 xl:grid-cols-5">
      <Field><label htmlFor="radar-status">Show</label><NativeSelect id="radar-status" name="status" defaultValue={filters.status}>{statuses.map((status) => <option key={status} value={status}>{status === "open" ? "All open" : status.charAt(0).toUpperCase() + status.slice(1)}</option>)}</NativeSelect></Field>
      <Field><label htmlFor="radar-category">Product category</label><Input id="radar-category" name="category" defaultValue={filters.category} maxLength={100} placeholder="All categories" /></Field>
      <Field><label htmlFor="radar-score">Minimum score</label><Input id="radar-score" name="minScore" type="number" min={0} max={100} step={1} defaultValue={filters.minScore ?? ""} placeholder="Any score" /></Field>
      <Field><label htmlFor="radar-watched">Topics</label><NativeSelect id="radar-watched" name="watched" defaultValue={filters.watched ? "true" : ""}><option value="">All topics</option><option value="true">Watched only</option></NativeSelect></Field>
      <div className="flex flex-wrap items-end gap-3"><Button type="submit" variant="secondary">Apply filters</Button>{filtered && <Link href={`/${handle}/opportunities`} className="inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11">Clear filters</Link>}</div>
    </form>
    {list.items.length ? <section aria-label="Ranked opportunities" className="grid items-start gap-5 xl:grid-cols-2">{list.items.map((item, index) => <OpportunityCard key={item.id} item={item} handle={handle} rank={index + 1} />)}</section> : <EmptyState icon={TrendingUp} title={filtered ? "No opportunities match these filters" : "No opportunities yet"} description={filtered ? "Try a different category or score, or return to all open opportunities." : "After the daily refresh, rising searches with matching products in stock will appear here. Watched topics can appear below the usual score cut-off."} />}
  </div>;
}
