import type { Asset, AssetHistory, AssetSet } from "./_lib/model";

export const brandId = "11111111-1111-1111-1111-111111111111";
export const campaignId = "22222222-2222-2222-2222-222222222222";
export function asset(overrides: Partial<Asset> = {}): Asset {
  return { id: "33333333-3333-3333-3333-333333333333", kind: "search_ad", campaignType: "search", language: "en", adGroup: "Everyday shoes", format: null, width: null, height: null, durationSeconds: null, productIds: ["shoe-1"], locked: false, checkState: "unchecked", version: 1, origin: "generated", editedByPerson: false, mediaUrl: null, updatedAt: "2026-10-16T06:00:00Z", content: { keywords: null, headlines: ["Made for your everyday", "Find your next pair", "Comfort for every step"], longHeadlines: null, descriptions: ["Explore everyday shoes from Acme.", "Find your fit with our collection."], path1: "shoes", path2: null, finalUrl: "https://example.com/shoes", businessName: null, callToAction: null, title: null, currentTitle: null, offerId: null, script: null }, disclosure: { rules: "2026-10", aiGenerated: true, syntheticMedia: false, watermark: null, platformLabels: [], blocker: null }, check: { state: "unchecked", score: null, passed: null, settingsVersion: null, checkedAt: null, issues: [], overridden: null, fixAttempts: 0, fixes: [] }, canOverride: false, canSendBack: false, ...overrides };
}
export function assetSet(assets: Asset[] = [asset()], overrides: Partial<AssetSet> = {}): AssetSet {
  const groups: AssetSet["groups"] = [];
  for (const entry of assets) {
    let group = groups.find((candidate) => candidate.campaignType === entry.campaignType && candidate.language === entry.language);
    if (!group) { group = { campaignType: entry.campaignType, label: entry.campaignType === "demand_gen" ? "Demand Gen" : entry.campaignType === "shopping" ? "Shopping" : "Search", language: entry.language, languageName: entry.language === "zxx" ? null : entry.language === "hi" ? "Hindi" : "English", assets: [] }; groups.push(group); }
    group.assets.push(entry);
  }
  return { campaignId, stage: "creating", canEdit: true, editBlocker: null, languages: [...new Set(assets.map((entry) => entry.language))].filter((code) => code !== "zxx"), groups, check: { score: null, passed: 0, flagged: 0, overridden: 0, unchecked: assets.length, settingsVersion: 0, passScore: 80, fixAttempts: 3, approveBlocker: null }, canSendBack: false, ...overrides };
}
export function history(entry = asset()): AssetHistory {
  return { assetId: entry.id, campaignId, kind: entry.kind, campaignType: entry.campaignType, language: entry.language, status: "active", locked: false, currentVersion: entry.version, versions: [{ version: entry.version, origin: "generated", createdAt: entry.updatedAt, createdBy: null, instruction: "Keep the brand voice", stageRunId: null, model: "gemini", promptVersion: "create-v1", agentCalls: [], sources: { productIds: ["shoe-1"], photoUrls: ["https://example.com/photo.jpg"], inputs: [] }, brandKitVersion: 2, briefVersion: 3, previousVersion: null, changedFields: [], approvedBy: null, approvedAt: null, memoryUses: [], content: entry.content, mediaUrl: entry.mediaUrl, disclosure: entry.disclosure }] };
}
