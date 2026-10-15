import { Eye } from "lucide-react";
import Link from "next/link";

import { growth, number } from "@/components/opportunities/format";
import { OpportunityProducts } from "@/components/opportunities/products";
import { OpportunityTrend } from "@/components/opportunities/trend";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import type { components } from "@/lib/core-api/schema";

export function OpportunityCard({ item, handle, rank }: { item: components["schemas"]["OpportunityCard"]; handle: string; rank: number }) {
  return <Card><CardContent className="flex min-w-0 flex-col gap-5 pt-4 md:pt-5">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-eyebrow text-subtle-foreground">Rank {rank}</p>
      <div className="flex flex-wrap gap-2">{item.watched && <Badge><Eye aria-hidden="true" className="size-3" />Watched</Badge>}<Badge variant={item.status === "accepted" ? "success" : "neutral"}>{item.status.charAt(0).toUpperCase() + item.status.slice(1)}</Badge></div>
    </div>
    <div className="flex items-start justify-between gap-4">
      <h2 className="min-w-0 font-display text-display-sm wrap-anywhere"><Link href={`/${handle}/opportunities/${item.id}`} className="text-primary hover:underline">{item.topic}</Link></h2>
      <p className="shrink-0 text-right"><span className="font-display text-display-md tabular-nums">{number(item.score)}</span><span className="block text-xs text-muted-foreground">Score / 100</span></p>
    </div>
    <dl className="grid grid-cols-2 gap-3 text-small"><div><dt className="text-muted-foreground">Demand growth</dt><dd className="font-semibold tabular-nums">{growth(item.growth)}</dd></div><div><dt className="text-muted-foreground">Last month’s searches</dt><dd className="font-semibold tabular-nums">{number(item.lastMonthSearches)}</dd></div></dl>
    <OpportunityTrend points={item.trend} compact label={`Search demand for ${item.topic}`} />
    <div className="rounded-md bg-muted p-3 text-small"><p className="mb-1 font-semibold">Why</p><p className="wrap-anywhere text-muted-foreground">{item.reason ?? "No supported reason is available yet. Open the opportunity to review its evidence."}</p></div>
    <p className="text-small text-muted-foreground"><span className="font-medium text-foreground">Rising regions: </span>{item.regions.map((region) => region.name).join(", ") || "No rising regions reported."}</p>
    <section aria-label={`Matching products for ${item.topic}`}><h3 className="text-sm font-semibold">Matching products · {number(item.productCount)}</h3><OpportunityProducts products={item.products} />{Number(item.productCount) > item.products.length && <p className="text-small text-muted-foreground">Open the opportunity for all matching products.</p>}</section>
  </CardContent></Card>;
}
