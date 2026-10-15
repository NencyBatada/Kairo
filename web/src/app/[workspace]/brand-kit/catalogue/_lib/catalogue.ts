import type { FormState } from "@/lib/forms";

import type { Availability, ImportStatus } from "./types";

export type Filters = { page: number; pageSize: number; status: "active" | "removed"; search?: string; category?: string; availability?: Availability; missing?: string };
export const availabilityLabels: Record<Availability, string> = { in_stock: "In stock", out_of_stock: "Out of stock", preorder: "Pre-order", backorder: "Back-order" };
export const missingLabels: Record<string, string> = { any: "Any detail", title: "Title", price: "Price", link: "Product link", image: "Image", stock: "Stock status", category: "Category", imageLink: "Image" };
export function catalogueFilters(query: Record<string, string | string[] | undefined>): Filters {
  const first = (key: string) => (Array.isArray(query[key]) ? query[key][0] : query[key])?.trim();
  const value = Number(first("page"));
  const availability = first("availability");
  const missing = first("missing");
  return {
    page: Number.isSafeInteger(value) && value > 0 && value <= 1_000_000 ? value : 1, pageSize: 25,
    status: first("status") === "removed" ? "removed" : "active",
    ...(first("search") ? { search: first("search")!.slice(0, 200) } : {}),
    ...(first("category") ? { category: first("category")!.slice(0, 500) } : {}),
    ...(availability && Object.hasOwn(availabilityLabels, availability) ? { availability: availability as Availability } : {}),
    ...(missing && ["any", "title", "price", "link", "image", "stock"].includes(missing) ? { missing } : {}),
  };
}
export function catalogueQuery(filters: Filters, page = filters.page) {
  const query = new URLSearchParams({ page: String(page), pageSize: String(filters.pageSize), status: filters.status });
  for (const key of ["search", "category", "availability", "missing"] as const) if (filters[key]) query.set(key, filters[key]);
  return query.toString();
}
export function importOutcome(result: { status: ImportStatus; errorMessage?: string | null }): FormState {
  if (result.status === "suspicious") return { status: "error", message: "The import was held because it could remove too many products or has no valid rows. Products are unchanged. Review the import before trying again." };
  if (result.status === "failed") return { status: "error", message: result.errorMessage ?? "The catalogue could not be imported. Review the file or feed and try again." };
  return { status: "saved", message: result.status === "unchanged" ? "The catalogue is already up to date." : "Catalogue imported." };
}
export function safeProductUrl(value: string | null | undefined) {
  try { const url = new URL(value ?? ""); return ["http:", "https:"].includes(url.protocol) ? url.href : undefined; } catch { return undefined; }
}
