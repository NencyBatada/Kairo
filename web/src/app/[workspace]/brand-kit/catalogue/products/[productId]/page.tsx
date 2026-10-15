import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { PageHeader } from "@/components/page-header";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { ProductFixes } from "../../_components/product-fixes";
import { catalogueJson, catalogueRequest } from "../../_lib/api";
import { availabilityLabels, safeProductUrl } from "../../_lib/catalogue";
import type { ProductDetail } from "../../_lib/types";
import { fixProduct } from "../../actions";

export const metadata: Metadata = { title: "Catalogue product" };
export default async function ProductPage({ params }: { params: Promise<{ workspace: string; productId: string }> }) {
  const { workspace: handle, productId } = await params;
  const workspace = await getWorkspace(handle);
  const response = await catalogueRequest(workspace.id, `/products/${encodeURIComponent(productId)}`);
  if (response.status === 404) notFound();
  const [product, details] = await Promise.all([
    catalogueJson<ProductDetail>(response),
    coreApi().then((api) => api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } })).then(unwrap),
  ]);
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: details.timeZone });
  const link = safeProductUrl(product.link); const image = safeProductUrl(product.imageLink);
  return <div className="flex flex-col gap-8">
    <PageHeader title={product.title || "Untitled product"} eyebrow="Catalogue" description={`Product ID: ${product.offerId}`} />
    <Link href={`/${handle}/brand-kit/catalogue`} className="self-start text-small text-primary hover:underline">Back to catalogue</Link>
    <div className="flex flex-wrap gap-3"><Badge variant={product.status === "active" ? "neutral" : "warning"}>{product.status === "active" ? "Active" : "Removed from source"}</Badge><Badge variant={product.availability === "in_stock" ? "success" : "warning"}>{product.availability ? availabilityLabels[product.availability] : "Stock missing"}</Badge></div>
    <p className="max-w-prose text-sm wrap-anywhere text-muted-foreground">{product.description || "No description supplied."}</p>
    <div className="flex flex-wrap gap-4">{link && <a href={link} target="_blank" rel="noopener noreferrer" className="text-sm text-primary hover:underline">View product</a>}{image && <a href={image} target="_blank" rel="noopener noreferrer" className="text-sm text-primary hover:underline">View product image</a>}</div>
    <ProductFixes key={product.id} product={product} canFix={workspace.roles.some((role) => role === "admin" || role === "marketer")} action={fixProduct.bind(null, workspace.id, productId)} />
    <Card><CardHeader><CardTitle>Recent stock changes</CardTitle></CardHeader><CardContent>{!product.stockEvents?.length ? <p className="text-small text-muted-foreground">No stock changes recorded.</p> : <ul className="divide-y divide-border">{product.stockEvents.map((event, i) => <li key={event.id ?? i} className="py-3 text-small wrap-anywhere">{event.from?.replaceAll("_", " ") ?? "Not recorded"} to {event.to.replaceAll("_", " ")} · {dates.format(new Date(event.at))}</li>)}</ul>}</CardContent></Card>
  </div>;
}
