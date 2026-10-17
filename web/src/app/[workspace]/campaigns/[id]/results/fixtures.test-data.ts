import type { Impact, Memory, Reading, Report } from "./_lib/report";

export const brandId = "11111111-1111-1111-1111-111111111111";
export const campaignId = "22222222-2222-2222-2222-222222222222";
export function reading(overrides: Partial<Reading> = {}): Reading {
  return { id: "reading", early: false, startDate: "2026-10-01", throughDate: "2026-10-14", kpi: "orders", unit: "city", days: 14, plannedDays: 14, status: "ok", reason: null, conclusive: true, lift: ".12", low: ".06", high: ".2", actual: 112, expected: 100, extra: "12", extraLow: "6", extraHigh: "20", placebos: 40, revenue: null, regions: [], daily: [], method: "placebo-did/1", createdAt: "2026-10-15T00:00:00Z", ...overrides };
}
export function report(overrides: Partial<Report> = {}): Report {
  return { id: "report", status: "ready", title: "Rain campaign", kpi: "orders", currency: "INR", spend: "1200", results: "30", costPerResult: "40", costPerExtra: "100", costPerExtraLow: "60", costPerExtraHigh: "200", targetCost: null, outcome: "worked", nextStep: "scale_up", nextTest: null, flags: [], summary: "The campaign increased orders.", nextStepReasoning: "Extra orders met the cost target.", inconclusiveReason: null, nextTestAdvice: null, model: "report-model", promptVersion: "report/1", readyAt: "2026-10-15T01:00:00Z", ...overrides };
}
export function impact(overrides: Partial<Impact> = {}): Impact {
  return { campaignId, stage: "results_ready", state: "ready", report: report(), final: reading(), ...overrides };
}
export function memory(overrides: Partial<Memory> = {}): Memory {
  return { id: "lesson", version: 1, kind: "message", outcome: "worked", title: "Quick-dry message", lesson: "Explain the quick-dry benefit.", evidence: {}, products: [], categories: [], regions: [], languages: [], sourceCampaignId: campaignId, state: "active", createdBy: "learn", createdAt: "2026-10-15T01:30:00Z", editedBy: null, editedAt: null, removedBy: null, removedAt: null, uses: 0, ...overrides };
}
