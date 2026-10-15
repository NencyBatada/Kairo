import type { CatalogueImport, CatalogueSummary, ProductDetail } from "./types";

export const product: ProductDetail = {
  id: "product-1", offerId: "RUN-01", title: "Trail runner", description: "Built for everyday runs.", link: "https://example.com/trail", imageLink: null,
  additionalImageLinks: [], price: null, salePrice: null, currency: "INR", availability: null, category: "Running", googleCategory: null,
  brand: "Acme", gtin: null, itemGroupId: null, fixed: [], missing: ["price", "image", "stock"], status: "active", updatedAt: "2026-10-15T09:00:00Z", stockEvents: [],
};
export const imported: CatalogueImport = { id: "import-1", kind: "file", fileName: "products.csv", status: "succeeded", rows: 2, valid: 2, invalid: 0, added: 2, updated: 0, removed: 0, unchanged: 0, problems: [], startedAt: "2026-10-15T09:00:00Z" };
export const summary: CatalogueSummary = { products: 2, active: 2, inStock: 1, outOfStock: 0, missing: { title: 0, price: 1, image: 1, link: 0, stock: 1, any: 1 }, categories: [{ name: "Running", products: 2 }], lastImport: imported, source: null };
