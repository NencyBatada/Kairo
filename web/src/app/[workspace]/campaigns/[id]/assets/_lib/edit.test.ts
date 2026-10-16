import { expect, test } from "vitest";
import { adLength, readEdit, version } from "./edit";

function editForm(values: Record<string, string | string[] | undefined> = {}) {
  const form = new FormData();
  for (const [key, value] of Object.entries({ version: "1", headlines: "Made for your everyday\nFind your next pair\nComfort for every step", descriptions: "Explore everyday shoes from Acme.\nFind your fit with our collection.", path1: "shoes", path2: "", ...values })) if (value !== undefined) for (const entry of Array.isArray(value) ? value : [value]) form.append(key, entry);
  return form;
}
test.each(["0", "1.1", "1e2", "-1", "2147483648", "", null])("rejects invalid version %s", (value) => expect(version(value)).toBeNull());
test("counts Google Ads wide characters and Unicode code points like the backend", () => { expect(adLength("नमस्ते")).toBe(6); expect(adLength("好")).toBe(2); expect(adLength("😀")).toBe(1); expect(version("2147483647")).toBe(2147483647); });
test("reads only editable search fields, clears paths and tidies spacing", () => {
  const { body, errors } = readEdit("search_ad", editForm({ headlines: "  Made for everyday  \nFind your next pair\nComfort for every step", path1: "", finalUrl: "https://attacker.example", title: "Ignored" }));
  expect(errors).toEqual({}); expect(body.headlines?.[0]).toBe("Made for everyday"); expect(body.path1).toBe(""); expect(body.title).toBeNull(); expect(body).not.toHaveProperty("finalUrl");
});
test.each([{ headlines: "Only one" }, { headlines: `${"a".repeat(31)}\nSecond\nThird` }, { headlines: "Same\nsame\nThird" }, { descriptions: "Only one" }, { path1: "bad path" }, { path1: "", path2: "second" }])("validates search constraints %o", (values) => expect(Object.keys(readEdit("search_ad", editForm(values)).errors).length).toBeGreaterThan(0));
test("Demand Gen requires a short headline, business name and supported call to action", () => {
  const valid = editForm({ headlines: "Find your next pair", longHeadlines: "Discover shoes made for your everyday", descriptions: "Find your fit.", businessName: "Acme", callToAction: "shop_now" });
  expect(readEdit("demand_gen_text", valid).errors).toEqual({}); valid.set("headlines", "a".repeat(35)); expect(readEdit("demand_gen_text", valid).errors.headlines).toBeTruthy(); valid.set("businessName", ""); valid.set("callToAction", "unknown"); expect(readEdit("demand_gen_text", valid).errors).toHaveProperty("businessName"); expect(readEdit("demand_gen_text", valid).errors.callToAction).toBeTruthy();
});
test("shopping title allows 150 weighted characters", () => { expect(readEdit("shopping_title", editForm({ title: "a".repeat(150) })).errors).toEqual({}); expect(readEdit("shopping_title", editForm({ title: "好".repeat(76) })).errors.title).toBeTruthy(); });
test("keywords preserve match types and validate count, uniqueness and words", () => {
  const form = editForm({ keywordText: ["  everyday   shoes ", "walking shoes"], keywordMatch: ["phrase", "exact"] });
  expect(readEdit("keyword_set", form).errors).toEqual({}); expect(readEdit("keyword_set", form).body.keywords).toEqual([{ text: "everyday shoes", match: "phrase" }, { text: "walking shoes", match: "exact" }]);
  form.set("keywordText", "word ".repeat(11)); expect(readEdit("keyword_set", form).errors.keywords).toBeTruthy();
  expect(readEdit("keyword_set", editForm({ keywordText: ["Shoes", "shoes"], keywordMatch: ["broad", "exact"] })).errors.keywords).toBeTruthy();
  expect(readEdit("keyword_set", editForm({ keywordText: "shoes", keywordMatch: "unknown" })).errors.keywords).toBeTruthy();
  expect(readEdit("keyword_set", editForm()).errors.keywords).toBeTruthy();
});
test("media cannot be edited as text", () => { expect(readEdit("image", editForm()).errors.kind).toBeTruthy(); });
