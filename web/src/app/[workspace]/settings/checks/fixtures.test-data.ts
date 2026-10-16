import type { Settings } from "./_lib/settings";
export const settings: Settings = {
  version: 0, passScore: 80, fixAttempts: 3, updatedAt: null, updatedBy: null,
  rules: [
    { id: "format.text_limits", name: "Google Ads text limits", description: "Text stays within placement limits.", appliesTo: ["text"], severity: "block", defaultSeverity: "block", adjustable: false, overridable: false },
    { id: "policy.political", name: "Political content", description: "Political content needs verification Kairo does not do.", appliesTo: ["text", "image", "video"], severity: "block", defaultSeverity: "block", adjustable: false, overridable: false },
    { id: "brand.voice", name: "Brand voice", description: "Copy and voiceover follow the brand voice.", appliesTo: ["text", "video"], severity: "warn", defaultSeverity: "warn", adjustable: true, overridable: true },
  ], customRules: [],
};
