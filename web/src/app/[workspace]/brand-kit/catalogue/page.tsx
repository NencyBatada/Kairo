import type { Metadata } from "next";
import Link from "next/link";

import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { CatalogueSource } from "./_components/catalogue-source";
import { ImportResult } from "./_components/import-result";
import { ProductList } from "./_components/product-list";
import { catalogueJson, catalogueRequest } from "./_lib/api";
import { catalogueFilters, catalogueQuery } from "./_lib/catalogue";
import type { CatalogueImport, CatalogueSummary, ProductPage } from "./_lib/types";
import { refreshFeed, setFeed, stopFeed } from "./actions";

export const metadata: Metadata = { title: "Catalogue" };

export default async function CataloguePage({ params, searchParams }: { params: Promise<{ workspace: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const filters = catalogueFilters(await searchParams);
  const summaryResult = await catalogueRequest(workspace.id, "/summary");
  const header = <PageHeader title="Catalogue" eyebrow="Brand Kit" description="Your products, prices and stock, kept ready for every campaign." />;
  if (summaryResult.status === 404 || summaryResult.status === 501) return <div className="flex flex-col gap-8">{header}<Alert variant="info">Catalogue import is not available yet. This workspace can use the catalogue once its connection is ready.</Alert><Link href={`/${handle}/brand-kit`} className="text-sm text-primary hover:underline">Back to Brand Kit</Link></div>;
  const [summary, products, imports, details] = await Promise.all([
    catalogueJson<CatalogueSummary>(summaryResult),
    catalogueRequest(workspace.id, `/products?${catalogueQuery(filters)}`).then(catalogueJson<ProductPage>),
    catalogueRequest(workspace.id, "/imports").then(catalogueJson<CatalogueImport[]>),
    coreApi().then((api) => api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } })).then(unwrap),
  ]);
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: details.timeZone });
  return <div className="flex flex-col gap-8">{header}
    <Link href={`/${handle}/brand-kit`} className="self-start text-small font-medium text-primary hover:underline">Back to Brand Kit</Link>
    <dl className="grid grid-cols-2 gap-4 lg:grid-cols-4">{[["Active products", summary.active], ["In stock", summary.inStock], ["Out of stock", summary.outOfStock], ["Need details", summary.missing.any]].map(([label, value]) => <div key={label} className="rounded-lg border border-border bg-card p-4"><dt className="text-small text-muted-foreground">{label}</dt><dd className="mt-2 font-display text-display-md tabular-nums">{value}</dd></div>)}</dl>
    <CatalogueSource brandId={workspace.id} handle={handle} source={summary.source} canManage={workspace.roles.includes("admin")} setFeed={setFeed.bind(null, workspace.id)} refreshFeed={refreshFeed.bind(null, workspace.id)} stopFeed={stopFeed.bind(null, workspace.id)} />
    <ProductList products={products} filters={filters} categories={summary.categories} handle={handle} currency={details.currency} />
    {summary.lastImport && <Card><CardHeader><CardTitle>Latest import</CardTitle></CardHeader><CardContent><ImportResult result={summary.lastImport} handle={handle} /></CardContent></Card>}
    <Card><CardHeader><CardTitle>Import history</CardTitle></CardHeader><CardContent>{imports.length === 0 ? <p className="text-small text-muted-foreground">No imports yet.</p> : <ul className="divide-y divide-border">{imports.map((entry) => <li key={entry.id}><Link href={`/${handle}/brand-kit/catalogue/imports/${entry.id}`} className="flex min-w-0 flex-col gap-1 rounded-md p-3 text-small hover:bg-accent"><span className="font-medium wrap-anywhere">{entry.fileName ?? entry.feedUrl ?? "Catalogue import"}</span><span className="text-muted-foreground">{dates.format(new Date(entry.startedAt))} · {entry.status.charAt(0).toUpperCase() + entry.status.slice(1)}</span></Link></li>)}</ul>}</CardContent></Card>
  </div>;
}
