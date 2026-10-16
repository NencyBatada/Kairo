import type { BriefDetail, ProofResult, Proposal } from "./_lib/model";
import { room } from "../fixtures.test-data";

export const detail: BriefDetail = {
  campaignId: room.id, campaignVersion: 1, editable: true, budgetLimit: { amount: 100000, currency: "INR" }, warnings: [],
  brief: {
    version: 2, origin: "drafted", title: "Rain-ready trainers", objective: "Increase trainer orders", kpi: "orders", campaignTypes: ["search"],
    budget: { amount: 50000, currency: "INR" }, startDate: "2026-10-18", endDate: "2026-11-01", regions: ["Maharashtra"],
    audiences: ["City commuters"], keyMessages: ["Stay dry"], products: [{ id: "p1", title: "Rain Runner", price: 2000, currency: "INR", link: "https://example.com/runner", inStock: true }],
    landingPages: ["https://example.com/runner"], languages: ["en"], reasons: [{ field: "budget", reason: "Based on keyword bid ranges.", evidence: [{ label: "Bid range", value: "₹8 to ₹14", source: "https://example.com/keywords" }] }], model: null, promptVersion: null, createdAt: "2026-10-16T00:30:00Z",
  },
  options: { kpis: [{ value: "orders", label: "Orders" }], campaignTypes: [{ value: "search", label: "Search" }, { value: "shopping", label: "Shopping" }], languages: [{ value: "en", label: "English" }, { value: "hi", label: "Hindi" }], regions: [{ id: "mh", name: "Maharashtra", lastMonthSearches: 1000 }, { id: "gj", name: "Gujarat", lastMonthSearches: 500 }], products: [{ id: "p1", title: "Rain Runner", price: 2000, currency: "INR", link: "https://example.com/runner", inStock: true }] },
  history: [{ version: 2, origin: "drafted", by: null, instruction: null, createdAt: "2026-10-16T00:30:00Z" }],
};

export const proof: ProofResult = {
  updating: false, canChooseHoldouts: true, canChangeAfterLock: false,
  plan: { version: 1, briefVersion: 2, unit: "city", kpi: "orders", insideArea: true, chosenByUser: false,
    testRegions: [{ id: "mumbai", name: "Mumbai", state: "Maharashtra", latitude: 19.07, longitude: 72.87, inArea: true, matchScore: null, share: .4 }],
    holdouts: [{ id: "pune", name: "Pune", state: "Maharashtra", latitude: 18.52, longitude: 73.85, inArea: true, matchScore: .94, share: .1 }],
    candidates: [{ id: "pune", name: "Pune", state: "Maharashtra", latitude: 18.52, longitude: 73.85, inArea: true, matchScore: .94, share: .1 }, { id: "nashik", name: "Nashik", state: "Maharashtra", latitude: 19.99, longitude: 73.78, inArea: true, matchScore: .88, share: .08 }],
    window: { start: "2026-10-18", end: "2026-11-01", days: 15 }, minDetectableLift: .12, liftByWindow: [{ days: 14, lift: .13 }, { days: 21, lift: .1 }], holdoutShare: .1, holdoutCost: { amount: 1200, currency: "INR" }, dataDays: 56, placeboWindows: 4, warnings: [], lockedAt: null, changedAfterLock: false, approvedWithWarnings: false, changeReason: null, createdAt: "2026-10-16T00:31:00Z" },
};

export const proposal: Proposal = { id: "11111111-1111-4111-8111-111111111111", briefVersion: 2, instruction: "Add Hindi", summary: "Reach Hindi speakers", changes: [{ field: "languages", label: "Languages", before: "English", after: "English, Hindi", reason: "Your instruction." }], preview: { ...detail.brief!, languages: ["en", "hi"] }, warnings: [], expiresAt: "2026-10-16T01:30:00Z" };

export const draftRoom = { ...room, status: "in_progress", runs: [], actions: { ...room.actions, retryRun: null, requestApproval: "brief", requestApprovalBlocker: null } };

export function editForm(overrides: Record<string, string | string[]> = {}) {
  const form = new FormData();
  const values = { version: "1", briefVersion: "2", title: "Rain-ready trainers", objective: "Increase trainer orders", kpi: "orders", campaignTypes: ["search"], budget: "50000", startDate: "2026-10-18", endDate: "2026-11-01", regions: ["Maharashtra"], audiences: "City commuters", keyMessages: "Stay dry", productIds: ["p1"], landingPages: "https://example.com/runner", languages: ["en"], ...overrides };
  for (const [name, value] of Object.entries(values)) for (const item of Array.isArray(value) ? value : [value]) form.append(name, item);
  return form;
}
