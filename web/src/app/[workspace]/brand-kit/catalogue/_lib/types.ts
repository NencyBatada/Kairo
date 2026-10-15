import type { FormState } from "@/lib/forms";

export type ImportStatus = "succeeded" | "unchanged" | "failed" | "suspicious";
export type ImportProblem = { row: number; attribute: string; message: string };
export type CatalogueImport = {
  id: string; status: ImportStatus; kind: "file" | "feed"; fileName?: string | null; feedUrl?: string | null;
  rows: number; valid: number; invalid: number; added: number; updated: number; removed: number; unchanged: number;
  errorCode?: string | null; errorMessage?: string | null; problems?: ImportProblem[]; startedAt: string; finishedAt?: string | null;
};
export type CatalogueSource = { kind: "file" | "feed"; feedUrl?: string | null; lastCheckedAt?: string | null; lastSucceededAt?: string | null; failuresInRow: number };
export type CatalogueSummary = {
  products: number; active: number; inStock: number; outOfStock: number;
  missing: { title: number; price: number; link: number; image: number; stock: number; any: number };
  categories: { name: string; products: number }[]; lastImport: CatalogueImport | null; source: CatalogueSource | null;
};
export type Availability = "in_stock" | "out_of_stock" | "preorder" | "backorder";
export type FixField = "title" | "price" | "link" | "imageLink" | "category";
export type Product = {
  id: string; offerId: string; title: string | null; description: string | null; link: string | null; imageLink: string | null;
  additionalImageLinks: string[]; price: number | string | null; salePrice: number | string | null; currency: string | null;
  availability: Availability | null; category: string | null; googleCategory: string | null; brand: string | null;
  gtin: string | null; itemGroupId: string | null; fixed: string[]; missing: string[]; status: "active" | "removed"; updatedAt: string;
};
export type StockEvent = { id?: string; from: string | null; to: string; at: string; importId: string };
export type ProductDetail = Product & { fixes?: Partial<Record<FixField | "currency", string | number | null>>; stockEvents?: StockEvent[] };
export type ProductPage = { items: Product[]; page: number; pageSize: number; total: number };
export type CatalogueActionState = FormState & { import?: CatalogueImport };
export type CatalogueAction = (state: CatalogueActionState, data: FormData) => Promise<CatalogueActionState>;
