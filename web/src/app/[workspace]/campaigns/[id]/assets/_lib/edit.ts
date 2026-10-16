import type { components } from "@/lib/core-api/schema";
import type { Asset } from "./model";
import type { FormState } from "@/lib/forms";

export type AssetState = FormState & { asset?: Asset; conflict?: boolean; runId?: string };
export type AssetEdit = components["schemas"]["AssetEditRequest"];
export const textKinds = ["keyword_set", "search_ad", "demand_gen_text", "shopping_title"];
export const regenerationKinds = [...textKinds, "image", "video"];
export const callsToAction = ["learn_more", "shop_now", "buy_now", "order_now", "sign_up", "see_more", "visit_site", "book_now", "contact_us", "get_quote", "download", "subscribe", "apply_now"];

export function version(value: unknown) {
  return /^\d+$/.test(String(value)) && Number(value) >= 1 && Number(value) <= 2147483647 ? Number(value) : null;
}

export function tidy(value: string) { return value.trim().replace(/\s+/gu, " "); }

export function adLength(value: string) {
  return Array.from(value).reduce((sum, char) => {
    const point = char.codePointAt(0)!;
    const wide = [[0x1100, 0x115f], [0x2e80, 0x303e], [0x3041, 0x33ff], [0x3400, 0x4dbf], [0x4e00, 0x9fff], [0xa000, 0xa4cf], [0xac00, 0xd7a3], [0xf900, 0xfaff], [0xfe30, 0xfe4f], [0xff00, 0xff60], [0xffe0, 0xffe6], [0x20000, 0x3fffd]].some(([start, end]) => point >= start && point <= end);
    return sum + (wide ? 2 : 1);
  }, 0);
}

export function readEdit(kind: string, form: FormData) {
  const body: AssetEdit = { version: version(form.get("version")), keywords: null, headlines: null, longHeadlines: null, descriptions: null, path1: null, path2: null, businessName: null, callToAction: null, title: null };
  const errors: Record<string, string[]> = {};
  const text = (field: string) => tidy(typeof form.get(field) === "string" ? String(form.get(field)) : "");
  if (body.version === null) errors.version = ["Reload the asset before saving."];
  function lines(field: "headlines" | "longHeadlines" | "descriptions", min: number, max: number, limit: number) {
    const value = String(form.get(field) ?? "").split(/\r?\n/u).map(tidy);
    body[field] = value;
    if (value.length < min || value.length > max) errors[field] = [`Write ${min} to ${max} lines.`];
    else if (value.some((line) => !line || adLength(line) > limit)) errors[field] = [`Fill every line, using at most ${limit} characters each.`];
    else if (new Set(value.map((line) => line.toLowerCase())).size !== value.length) errors[field] = ["Make each line different."];
  }
  if (kind === "search_ad") {
    lines("headlines", 3, 15, 30); lines("descriptions", 2, 4, 90);
    for (const field of ["path1", "path2"] as const) { body[field] = text(field); if (adLength(body[field]) > 15 || /\s/u.test(body[field])) errors[field] = ["Use up to 15 characters with no spaces."]; }
    if (body.path2 && !body.path1) errors.path2 = ["Add the first path before the second."];
  } else if (kind === "demand_gen_text") {
    lines("headlines", 1, 5, 40); lines("longHeadlines", 1, 5, 90); lines("descriptions", 1, 5, 90);
    if (!errors.headlines && !body.headlines?.some((line) => adLength(line) <= 30)) errors.headlines = ["Keep at least one headline within 30 characters."];
    body.businessName = text("businessName"); body.callToAction = text("callToAction");
    if (!body.businessName || adLength(body.businessName) > 25) errors.businessName = ["Use a business name of up to 25 characters."];
    if (!callsToAction.includes(body.callToAction)) errors.callToAction = ["Choose a call to action from the list."];
  } else if (kind === "shopping_title") {
    body.title = text("title");
    if (!body.title || adLength(body.title) > 150) errors.title = ["Write a title of up to 150 characters."];
  } else if (kind === "keyword_set") {
    const texts = form.getAll("keywordText").map((value) => tidy(typeof value === "string" ? value : ""));
    const matches = form.getAll("keywordMatch").map(String);
    body.keywords = texts.map((value, index) => ({ text: value, match: matches[index] ?? "" }));
    if (texts.length < 1 || texts.length > 50 || texts.length !== matches.length) errors.keywords = ["Keep 1 to 50 keywords, each with a match type."];
    else if (texts.some((value) => !value || adLength(value) > 80 || value.split(" ").length > 10)) errors.keywords = ["Use up to 80 characters and 10 words per keyword."];
    else if (matches.some((value) => !["broad", "phrase", "exact"].includes(value))) errors.keywords = ["Choose broad, phrase or exact match."];
    else if (new Set(texts.map((value) => value.toLowerCase())).size !== texts.length) errors.keywords = ["Make each keyword different."];
  } else errors.kind = ["Only text assets can be edited inline."];
  return { body, errors };
}
