import { date, flagName, label, liftRange, money, nextName, number, numeric, outcomeName, percent, range, readingReason, reasonName, reportFigures, timestamp, type Impact } from "./report";

export type ReportSection = { title: string; paragraphs: string[] };
export type PdfContent = { title: string; brand: string; campaignId: string; reportId: string; prepared: string; outcome: string; lift: string; sections: ReportSection[] };

export function pdfContent(impact: Impact, brand: string, timeZone: string): PdfContent {
  const report = impact.report;
  if (impact.state !== "ready" || report?.status !== "ready") throw new Error("The report is not ready.");
  const reading = impact.final, measured = liftRange(reading);
  const sections: ReportSection[] = [];
  if (reading) sections.push({ title: "Measurement window and coverage", paragraphs: [`${date(reading.startDate)} to ${date(reading.throughDate)}`, `${number(Number(reading.days))} of ${number(Number(reading.plannedDays))} planned days`, `Measurement calculated ${timestamp(reading.createdAt, timeZone)}`] });
  sections.push({ title: "Final lift reading", paragraphs: [measured ? `Estimated lift in ${label(reading!.kpi).toLowerCase()}: ${percent(measured.value)} (90% range: ${percent(measured.low)} to ${percent(measured.high)})` : readingReason(reading?.reason ?? null), "Compared with holdout regions (areas left out so we can measure lift).", ...(measured && reading ? [`Actual outcomes in test regions: ${numeric(reading.actual) === null ? "Not available" : number(numeric(reading.actual)!)}`, `Expected outcomes without the campaign: ${numeric(reading.expected) === null ? "Not available" : number(numeric(reading.expected)!)}`] : [])] });
  const revenue = measured && reading?.revenue ? range(reading.revenue.lift, reading.revenue.low, reading.revenue.high) : null;
  if (revenue) sections.push({ title: "Revenue lift", paragraphs: [`${percent(revenue.value)} (90% range: ${percent(revenue.low)} to ${percent(revenue.high)})`] });
  sections.push({ title: "Spend and results", paragraphs: reportFigures(report, reading).map(figure => `${figure.label}: ${figure.value}${figure.note ? `\n${figure.note}` : ""}`) });
  sections.push({ title: "What happened", paragraphs: ["Drafted by Kairo", report.summary || "The service did not supply a summary."] });
  if (report.outcome === "inconclusive") sections.push({ title: "Inconclusive", paragraphs: [report.inconclusiveReason || "The evidence does not establish whether this campaign worked. This is not proof of failure."] });
  if (report.flags.length) sections.push({ title: "What may affect this result", paragraphs: report.flags.map(flag => `${flagName(flag.code)}\n${flag.detail}`) });
  sections.push({ title: `Recommended next step: ${nextName(report.nextStep)}`, paragraphs: [report.nextStepReasoning || "No further reasoning was supplied.", "This report recommends a next step. Ad changes still follow the recommendation approval process."] });
  if (report.nextTest) sections.push({ title: "Design the next test", paragraphs: [...(report.nextTest.days !== null ? [`Suggested length: ${number(Number(report.nextTest.days))} days.`] : []), ...(report.nextTest.addRegions ? ["Include more regions."] : []), ...report.nextTest.reasons.map(reasonName), ...(report.nextTestAdvice ? [report.nextTestAdvice] : [])] });
  if (reading?.regions.length) sections.push({ title: "Regional evidence", paragraphs: reading.regions.map(region => {
    const result = region.status === "ok" && region.figures ? range(region.figures.lift, region.figures.low, region.figures.high) : null;
    return `${region.name} · ${region.test ? "Test region" : "Holdout region"}\n${result ? `${percent(result.value)} (90% range: ${percent(result.low)} to ${percent(result.high)})` : "No complete regional estimate and range."}${numeric(region.cost) !== null ? `\nRecorded spend: ${money(numeric(region.cost)!, report.currency)}` : ""}`;
  }) });
  return { title: report.title, brand, campaignId: impact.campaignId, reportId: report.id, prepared: timestamp(report.readyAt, timeZone), outcome: outcomeName(report.outcome), lift: measured ? `${percent(measured.value)} (90% range: ${percent(measured.low)} to ${percent(measured.high)})` : "No reliable lift estimate", sections };
}

export function pdfText(value: string) {
  const graphemes = new Intl.Segmenter("en", { granularity: "grapheme" });
  return value.replace(/\S{13,}/gu, word => {
    const parts = [...graphemes.segment(word)].map(part => part.segment);
    return parts.map((part, index) => index > 0 && index % 12 === 0 ? `\u200b${part}` : part).join("");
  });
}

export function paragraphLead(value: string): [string, string] {
  const segments = [...new Intl.Segmenter("en", { granularity: "grapheme" }).segment(value)];
  if (segments.length <= 200) return [value, ""];
  const boundary = segments[200].index;
  return [value.slice(0, boundary), value.slice(boundary)];
}
