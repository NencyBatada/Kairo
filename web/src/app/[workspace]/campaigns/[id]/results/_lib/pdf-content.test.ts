import { readFileSync } from "node:fs";
import { expect, test } from "vitest";
import { impact, reading, report } from "../fixtures.test-data";
import { paragraphLead, pdfContent, pdfText } from "./pdf-content";
import { pdfTheme } from "./pdf-theme";
import { reportFigures } from "./report";

test("PDF figures match the Results presentation model without recalculating costs", () => {
  const value = impact(), content = pdfContent(value, "Acme", "Asia/Kolkata");
  const figures = content.sections.find(section => section.title === "Spend and results")!.paragraphs.join("\n");
  for (const figure of reportFigures(value.report!, value.final)) { expect(figures).toContain(`${figure.label}: ${figure.value}`); if (figure.note) expect(figures).toContain(figure.note); }
  expect(content.lift).toBe("+12.0% (90% range: +6.0% to +20.0%)");
  expect(content.prepared).toContain("6:30");
});
test.each(["measuring", "writing", "failed"])("unready reports cannot be exported (%s)", state => expect(() => pdfContent(impact({ state }), "Acme", "UTC")).toThrow("not ready"));
test("inconclusive reports preserve explanations, proposed tests and all flags", () => {
  const content = pdfContent(impact({ report: report({ outcome: "inconclusive", nextStep: "retest", inconclusiveReason: "The range includes zero.", nextTest: { days: 28, addRegions: true, reasons: ["wide_range", "few_regions"] }, nextTestAdvice: "Collect more comparison data.", flags: [{ code: "ended_early", detail: "Ended on day ten." }, { code: "changed_after_lock", detail: "One holdout changed." }] }) }), "Acme", "UTC");
  const text = JSON.stringify(content);
  for (const value of ["Inconclusive", "The range includes zero.", "Suggested length: 28 days.", "Include more regions.", "Collect more comparison data.", "Test ended early", "Ended on day ten.", "One holdout changed."]) expect(text).toContain(value);
});
test.each([{ high: null }, { early: true }, { status: "insufficient_data", reason: "no_data" }])("an unavailable final estimate is never exported as a standalone lift", overrides => { const content = pdfContent(impact({ final: reading(overrides) }), "Acme", "UTC"); expect(content.lift).toBe("No reliable lift estimate"); expect(JSON.stringify(content)).not.toContain("+12.0%"); });
test("no final reading or summary is represented explicitly", () => { const content = pdfContent(impact({ final: null, report: report({ summary: null }) }), "Acme", "UTC"); expect(content.lift).toBe("No reliable lift estimate"); expect(JSON.stringify(content)).toContain("The service did not supply a summary."); });
test("revenue and regional estimates include their complete ranges", () => { const content = pdfContent(impact({ final: reading({ revenue: { lift: .2, low: .1, high: .3, actual: 120, expected: 100, extra: 20, extraLow: 10, extraHigh: 30, conclusive: true, placebos: 40, spread: .1 }, regions: [{ id: "city", name: "Mumbai", test: true, status: "ok", reason: null, figures: { lift: .1, low: .01, high: .2, extra: 10, extraLow: 1, extraHigh: 20, actual: 110, expected: 100, conclusive: true, placebos: 40, spread: .1 }, cost: 100 }] }) }), "Acme", "UTC"); expect(JSON.stringify(content)).toContain("+20.0% (90% range: +10.0% to +30.0%)"); expect(JSON.stringify(content)).toContain("+10.0% (90% range: +1.0% to +20.0%)"); });
test("long text wraps without splitting Hindi grapheme clusters or dropping content", () => { const text = "CampaignTitle".repeat(30), wrapped = pdfText(text); expect(wrapped).toContain("\u200b"); expect(wrapped.replaceAll("\u200b", "")).toBe(text); const hindi = "क्षेत्र".repeat(40); expect(pdfText(hindi).replaceAll("\u200b", "")).toBe(hindi); });
test("print colours remain the matching light design tokens", () => { const css = readFileSync("src/app/globals.css", "utf8").split("@media")[0]; for (const [name, colour] of Object.entries(pdfTheme)) expect(css).toContain(`--${name.replace(/[A-Z]/g, char => `-${char.toLowerCase()}`)}: ${colour}`); });

test("a bounded paragraph opening stays with its heading without losing or splitting graphemes", () => { const text = "क्षेत्र में बारिश और अभियान। ".repeat(30), [lead, rest] = paragraphLead(text); expect(lead + rest).toBe(text); expect([...new Intl.Segmenter("en", { granularity: "grapheme" }).segment(lead)]).toHaveLength(200); expect(paragraphLead("Short paragraph")).toEqual(["Short paragraph", ""]); });
