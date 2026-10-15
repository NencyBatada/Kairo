import type { components } from "@/lib/core-api/schema";

export const opportunity: components["schemas"]["OpportunityCard"] = {
  id: "opportunity-1", topic: "Waterproof trainers", status: "new", source: "search", score: "82", growth: "0.42", mom: 0.2, yoy: null,
  lastMonthSearches: "12000", avgMonthlySearches: 8000,
  trend: Array.from({ length: 12 }, (_, index) => ({ month: `2025-${String(index + 1).padStart(2, "0")}`, searches: (index + 1) * 1000 })),
  regions: [{ id: "region-1", name: "Maharashtra", lastMonthSearches: 7000, growth: 0.4, rising: true }],
  products: [{ id: "product-1", offerId: "trainer-1", title: "Rain runner", category: "Trainers", link: "https://shop.example/rain-runner", imageLink: "https://shop.example/rain-runner.jpg", price: "2499", salePrice: null, currency: "INR" }],
  productCount: 1, reason: "Search demand is rising ahead of the rains.", watched: true, available: true,
  firstSeenAt: "2026-10-14T00:00:00Z", lastSeenAt: "2026-10-15T01:00:00Z", savedAt: null, trigger: null,
};
