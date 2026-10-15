import Link from "next/link";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";

import { availabilityLabels, catalogueQuery, missingLabels, type Filters } from "../_lib/catalogue";
import type { ProductPage } from "../_lib/types";

export function ProductList({ products, filters, categories, handle, currency }: { products: ProductPage; filters: Filters; categories: { name: string; products: number }[]; handle: string; currency: string }) {
  const path = `/${handle}/brand-kit/catalogue`;
  return <Card><CardHeader><CardTitle>Products</CardTitle></CardHeader><CardContent className="flex min-w-0 flex-col gap-5">
    <form key={catalogueQuery(filters)} action={path} method="get" className="grid min-w-0 gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <div className="flex min-w-0 flex-col gap-1.5"><Label htmlFor="product-search">Search title or product ID</Label><Input id="product-search" name="search" defaultValue={filters.search} maxLength={200} /></div>
      <div className="flex min-w-0 flex-col gap-1.5"><Label htmlFor="product-category">Category</Label><NativeSelect id="product-category" name="category" defaultValue={filters.category ?? ""}><option value="">All categories</option>{filters.category && !categories.some((c) => c.name === filters.category) && <option value={filters.category}>{filters.category}</option>}{categories.map((category) => <option key={category.name} value={category.name}>{category.name}</option>)}</NativeSelect></div>
      <div className="flex min-w-0 flex-col gap-1.5"><Label htmlFor="product-stock">Stock</Label><NativeSelect id="product-stock" name="availability" defaultValue={filters.availability ?? ""}><option value="">All stock states</option>{Object.entries(availabilityLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</NativeSelect></div>
      <div className="flex min-w-0 flex-col gap-1.5"><Label htmlFor="product-missing">Missing details</Label><NativeSelect id="product-missing" name="missing" defaultValue={filters.missing ?? ""}><option value="">All products</option>{["any", "title", "price", "link", "image", "stock"].map((value) => <option key={value} value={value}>{missingLabels[value]}</option>)}</NativeSelect></div>
      <div className="flex min-w-0 flex-col gap-1.5"><Label htmlFor="product-status">Product status</Label><NativeSelect id="product-status" name="status" defaultValue={filters.status}><option value="active">Active</option><option value="removed">Removed</option></NativeSelect></div>
      <div className="flex items-end gap-4"><Button type="submit" variant="secondary">Filter products</Button><Link href={path} className="inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11">Clear filters</Link></div>
    </form>
    {products.items.length === 0 ? <p className="text-sm text-muted-foreground">No products match these filters.</p> : <div role="region" aria-label="Product table" tabIndex={0} className="overflow-x-auto rounded-md border border-border">
      <table className="w-full min-w-140 text-left text-sm"><caption className="sr-only">Imported products and missing details</caption><thead className="bg-muted text-xs text-subtle-foreground"><tr>{["Product", "Price", "Stock", "Missing details"].map((name) => <th scope="col" key={name} className="px-4 py-3 font-medium">{name}</th>)}</tr></thead><tbody className="divide-y divide-border">
        {products.items.map((product) => <tr key={product.id}><td className="max-w-64 px-4 py-3"><Link href={`${path}/products/${encodeURIComponent(product.id)}`} className="font-medium wrap-anywhere text-primary hover:underline">{product.title || "Title missing"}</Link><p className="mt-1 font-mono text-xs wrap-anywhere text-muted-foreground">{product.offerId}</p><p className="mt-1 text-small wrap-anywhere text-muted-foreground">{product.category || "No category"}</p></td><td className="px-4 py-3 tabular-nums">{product.price === null ? <span className="text-warning-strong">Price missing</span> : new Intl.NumberFormat("en-IN", { style: "currency", currency: product.currency ?? currency }).format(Number(product.price))}</td><td className="px-4 py-3"><Badge variant={product.availability === "in_stock" ? "success" : "warning"}>{product.availability ? availabilityLabels[product.availability] : "Stock missing"}</Badge></td><td className="max-w-64 px-4 py-3"><span className={product.missing.length ? "text-warning-strong" : "text-muted-foreground"}>{product.missing.length ? product.missing.map((field) => missingLabels[field] ?? field).join(", ") : "Complete"}</span>{product.fixed.length > 0 && <p className="mt-1 text-small text-muted-foreground">{product.fixed.length} {product.fixed.length === 1 ? "fix" : "fixes"} applied</p>}</td></tr>)}
      </tbody></table>
    </div>}
    <nav aria-label="Product pages" className="flex flex-wrap items-center justify-between gap-3 text-small"><p className="text-muted-foreground">{products.total} products · Page {products.page} of {Math.max(1, Math.ceil(products.total / products.pageSize))}</p><div className="flex gap-4">{products.page > 1 && <Link className="inline-flex min-h-9 items-center text-primary hover:underline pointer-coarse:min-h-11" href={`${path}?${catalogueQuery(filters, products.page - 1)}`}>Previous page</Link>}{products.page * products.pageSize < products.total && <Link className="inline-flex min-h-9 items-center text-primary hover:underline pointer-coarse:min-h-11" href={`${path}?${catalogueQuery(filters, products.page + 1)}`}>Next page</Link>}</div></nav>
  </CardContent></Card>;
}
