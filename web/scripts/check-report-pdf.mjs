import assert from "node:assert/strict";
import fs from "node:fs";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const ts = require("typescript");
require.extensions[".ts"] = (module, filename) => module._compile(ts.transpileModule(fs.readFileSync(filename, "utf8"), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true } }).outputText, filename);
require.extensions[".tsx"] = require.extensions[".ts"];
const base = "../src/app/[workspace]/campaigns/[id]/results/";
const { impact, reading, report } = require(base + "fixtures.test-data.ts");
const { pdfContent } = require(base + "_lib/pdf-content.ts");
const { ImpactPdfDocument } = require(base + "_lib/pdf-document.tsx");
const { renderToBuffer } = require("@react-pdf/renderer");
const { cloneElement } = require("react");
const scenarios = [
  impact(),
  impact({ report: report({ outcome: "inconclusive", nextStep: "retest", summary: "The range includes zero, so the test does not establish an effect.", nextStepReasoning: "Collect more evidence before deciding whether to scale up.", inconclusiveReason: "The range includes zero.", nextTest: { days: 28, addRegions: true, reasons: ["wide_range"] } }), final: reading({ low: -.03 }) }),
  impact({ final: reading({ status: "insufficient_data", reason: "no_data", lift: null, low: null, high: null }), report: report({ outcome: "inconclusive", nextStep: "retest", summary: "The loaded data did not cover the test well enough to measure an effect.", nextStepReasoning: "Load the missing data and design another test.", costPerExtra: null }) }),
  impact({ report: report({ title: "बारिश में अभियान और प्रभाव\n" + "W".repeat(80), summary: ("यह अभियान मुंबई और दिल्ली में चलाया गया। " + "LongCampaignIdentifier".repeat(15) + "\n").repeat(16), flags: [{ code: "ended_early", detail: "The test ended before its planned date." }] }) }),
];
let totalPages = 0;
for (const [index, scenario] of scenarios.entries()) {
  let pages;
  const content = pdfContent(scenario, "Acme / अपना ब्रांड", "Asia/Kolkata");
  const document = cloneElement(ImpactPdfDocument({ content }), { onRender: ({ _INTERNAL__LAYOUT__DATA_: layout }) => { pages = layout.children; } });
  const bytes = await renderToBuffer(document);
  assert.equal(bytes.subarray(0, 5).toString(), "%PDF-");
  assert.ok(bytes.length > 1000);
  assert.ok(pages.length > 0 && pages.length < 25);
  for (const page of pages) {
    assert.ok(Math.abs(page.box.width - 595.28) < 1 && Math.abs(page.box.height - 841.89) < 1);
    const footer = page.children.find(node => node.props?.fixed);
    assert.ok(footer && footer.box.top >= page.box.height - 55 && footer.box.top + footer.box.height < page.box.height - 10, `Scenario ${index + 1}: footer is outside its page`);
    for (const node of page.children.filter(node => node.children?.[0]?.type === "VIEW")) { const group = node.children[0]; if (group.children?.[0]?.box.height > 0) assert.ok(group.children[1]?.box.height > 0, `Scenario ${index + 1}: a section heading has no following text on its page`); }
    for (const node of page.children.filter(node => !node.props?.fixed)) assert.ok(node.box.top >= 0 && node.box.top + node.box.height <= page.box.height - 50, `Scenario ${index + 1}: content extends outside its printable area`);
  }
  totalPages += pages.length;
  if (process.env.KAIRO_PDF_QA_OUTPUT) {
    fs.mkdirSync(process.env.KAIRO_PDF_QA_OUTPUT, { recursive: true });
    fs.writeFileSync(`${process.env.KAIRO_PDF_QA_OUTPUT}/report-${index + 1}.pdf`, bytes);
  }
}
console.log(`Rendered ${scenarios.length} report scenarios across ${totalPages} A4 pages with valid footers and printable bounds.`);
