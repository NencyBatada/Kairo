import Image from "next/image";

import type { components } from "@/lib/core-api/schema";

import { webUrl } from "./format";

type Product = components["schemas"]["MatchedProductView"];

export function OpportunityProducts({ products }: { products: Product[] }) {
  if (!products.length) return <p className="text-small text-muted-foreground">No matching products are currently in stock.</p>;
  return <ul className="divide-y divide-border">{products.map((product) => {
    const image = webUrl(product.imageLink);
    const link = webUrl(product.link);
    const price = product.salePrice ?? product.price;
    const amount = price !== null && product.currency ? new Intl.NumberFormat("en-IN", { style: "currency", currency: product.currency }).format(Number(price)) : null;
    return <li key={product.id} className="flex min-w-0 gap-3 py-3">
      {image ? <Image unoptimized src={image} alt="" width={56} height={56} className="size-14 shrink-0 rounded-md border border-border bg-lightbox object-contain" /> : <span className="flex size-14 shrink-0 items-center justify-center rounded-md bg-muted text-center text-xs text-muted-foreground">No image</span>}
      <div className="min-w-0 text-small">
        {link ? <a href={link} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-6 items-center font-medium wrap-anywhere text-primary hover:underline pointer-coarse:min-h-11">{product.title}<span className="sr-only"> (opens in a new tab)</span></a> : <p className="font-medium wrap-anywhere">{product.title}</p>}
        <p className="text-muted-foreground tabular-nums">{amount ?? "Price not available"} · In stock</p>
      </div>
    </li>;
  })}</ul>;
}
