import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

export type Settings = components["schemas"]["CheckSettingsView"];
export type SettingsState = FormState & { settings?: Settings; conflict?: boolean };
export type CustomRule = { id: string | null; text: string; appliesTo: string; severity: string };
export type Draft = { passScore: string; fixAttempts: string; rules: Record<string, string>; customRules: CustomRule[] };
export const severityNames: Record<string, string> = { block: "Block", warn: "Warn", off: "Off" };
export const targetNames: Record<string, string> = { text: "Text", image: "Image", video: "Video", all: "All assets" };
export function toDraft(settings: Settings): Draft {
  return { passScore: String(settings.passScore), fixAttempts: String(settings.fixAttempts), rules: Object.fromEntries(settings.rules.filter(rule => rule.adjustable).map(rule => [rule.id, rule.severity])), customRules: settings.customRules.map(rule => ({ ...rule })) };
}
export function integer(value: unknown, max: number) {
  return (typeof value === "string" || typeof value === "number") && /^\d+$/.test(String(value)) && Number(value) <= max ? Number(value) : null;
}
export function readSettings(input: unknown, settings: Settings) {
  const errors: Record<string, string[]> = {};
  const value = input && typeof input === "object" ? input as Partial<Draft> : {};
  const passScore = integer(value.passScore, 100), fixAttempts = integer(value.fixAttempts, 5);
  if (passScore === null) errors.passScore = ["Enter a whole-number pass score from 0 to 100."];
  if (fixAttempts === null) errors.fixAttempts = ["Enter a whole-number fix limit from 0 to 5."];
  const rules: Record<string, string> = {};
  const entries = value.rules && typeof value.rules === "object" && !Array.isArray(value.rules) ? Object.entries(value.rules) : [];
  for (const [id, severity] of entries) {
    const rule = settings.rules.find(entry => entry.id === id);
    if (!rule || !rule.adjustable || !["block", "warn", "off"].includes(severity)) errors[`rules.${id}`] = ["Choose block, warn or off for an adjustable rule."];
    else rules[id] = severity;
  }
  for (const rule of settings.rules.filter(rule => rule.adjustable)) if (!Object.hasOwn(rules, rule.id)) errors[`rules.${rule.id}`] = ["Choose block, warn or off."];
  const customRules: CustomRule[] = [];
  const custom = Array.isArray(value.customRules) ? value.customRules : [];
  if (!Array.isArray(value.customRules) || custom.length > 20) errors.customRules = ["Add up to 20 custom rules."];
  const ids = new Set<string>(), texts = new Set<string>();
  for (const [index, raw] of custom.slice(0, 20).entries()) {
    const entry = raw && typeof raw === "object" ? raw : {} as CustomRule;
    const text = typeof entry.text === "string" ? entry.text.trim().replace(/\s+/gu, " ") : "";
    const prefix = `customRules[${index}]`;
    if (entry.id !== null && (typeof entry.id !== "string" || !settings.customRules.some(rule => rule.id === entry.id) || ids.has(entry.id))) errors[`${prefix}.id`] = ["This rule changed. Reload the settings before saving."];
    if (typeof entry.id === "string") ids.add(entry.id);
    if (text.length < 3 || text.length > 200) errors[`${prefix}.text`] = ["Describe the rule in 3 to 200 characters."];
    else if (texts.has(text.toLowerCase())) errors[`${prefix}.text`] = ["This rule is already in the list."];
    texts.add(text.toLowerCase());
    if (!["text", "image", "video", "all"].includes(entry.appliesTo)) errors[`${prefix}.appliesTo`] = ["Choose text, image, video or all assets."];
    if (!["block", "warn"].includes(entry.severity)) errors[`${prefix}.severity`] = ["Choose block or warn."];
    customRules.push({ id: entry.id ?? null, text, appliesTo: entry.appliesTo, severity: entry.severity });
  }
  return { errors, body: { passScore, fixAttempts, rules, customRules } };
}
