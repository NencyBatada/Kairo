"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { amount, canRemember, csv, dailyRows, date, label, lift, outcomeNames, percent, range, validId, type Analysis } from "../_lib/model";
import { AnalysisChart } from "./analysis-chart";
import { actionClass, control } from "./setup-form";

export function downloadFile(content: string, name: string) {
  const url = URL.createObjectURL(new Blob([content], { type: "text/csv;charset=utf-8" }));
  try { const link = document.createElement("a"); link.href = url; link.download = name; document.body.append(link); link.click(); link.remove(); }
  finally { setTimeout(() => URL.revokeObjectURL(url), 1000); }
}

export function AnalysisResult({ analysis, workspace, canEdit, pending, blocked, onRemember }: { analysis: Analysis; workspace: string; canEdit: boolean; pending: boolean; blocked: boolean; onRemember: () => void }) {
  const result = analysis.result, [message, setMessage] = useState(""), [manual, setManual] = useState<string | null>(null), [copying, setCopying] = useState(false), notice = useRef<HTMLParagraphElement>(null);
  useEffect(() => { if (message) notice.current?.focus(); }, [message]);
  if (analysis.state !== "result" || !result) return <Alert variant="neutral">A completed result is not available yet. Check the analysis state or refresh.</Alert>;
  const figures = result.status === "ok" ? result.figures : null, complete = figures && range(figures.lift, figures.low, figures.high), extra = figures && range(figures.extra, figures.extraLow, figures.extraHigh);
  const rangeMin = Math.min(complete?.low ?? 0, 0), rangeMax = Math.max(complete?.high ?? 0, 0), rangeWidth = rangeMax - rangeMin || 1;
  async function copy() {
    if (copying) return;
    const url = new URL(`/${workspace}/impact-lab/${analysis.id}`, window.location.origin).href;
    setCopying(true);
    try { await navigator.clipboard.writeText(url); setMessage("Workspace link copied."); setManual(null); }
    catch { setManual(url); setMessage("Copy the workspace link from the field below."); }
    finally { setCopying(false); }
  }
  return <section className="min-w-0 space-y-8" aria-labelledby="lab-result-title">
    <div className="space-y-4 rounded-lg border border-border bg-card p-5 md:p-6"><div className="flex flex-wrap items-center gap-3"><h2 id="lab-result-title" className="text-base font-semibold">Estimated impact</h2><Badge variant="neutral">{outcomeNames[result.outcome] ?? "Outcome unavailable"}</Badge><Badge variant="neutral">{result.conclusive ? "Conclusive" : "Inconclusive"}</Badge></div>
      <p className="font-display text-display-sm wrap-anywhere">{complete ? lift({ lift: complete.value, low: complete.low, high: complete.high }) : "Not enough data for a complete estimate and 90% range."}</p>
      {complete && <div className="space-y-2"><div role="img" aria-label={`Estimated effect ${percent(complete.value)}; 90% range ${percent(complete.low)} to ${percent(complete.high)}. The vertical line marks zero effect.`} className="relative mx-2 h-8"><div className="absolute inset-x-0 top-4 border-t border-border-strong" /><span className="absolute top-2 h-4 border-l border-foreground" style={{ left: `${-rangeMin / rangeWidth * 100}%` }} /><span className="absolute top-3 h-2 rounded-full border border-info bg-info-soft" style={{ left: `${(complete.low - rangeMin) / rangeWidth * 100}%`, width: `${(complete.high - complete.low) / rangeWidth * 100}%` }} /><span className="absolute top-2.5 size-3 -translate-x-1/2 rounded-full bg-info" style={{ left: `${(complete.value - rangeMin) / rangeWidth * 100}%` }} /></div><p className="text-small text-muted-foreground">The vertical line marks zero effect; the band shows the 90% confidence range.</p></div>}
      <p className="text-small text-muted-foreground">Outcome: {label(analysis.source.metric)}{result.currency ? ` (${result.currency})` : ""} · Change {date(analysis.start)} to {date(analysis.end)}{analysis.data ? ` · Loaded through ${date(analysis.data.to)}` : ""}</p>
      <dl className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">{[
        ["Actual during the change", amount(figures?.actual, result.currency)],
        ["Expected without the change", amount(figures?.expected, result.currency)],
        ["Estimated extra outcomes", extra ? `${amount(extra.value, result.currency)} (90% range: ${amount(extra.low, result.currency)} to ${amount(extra.high, result.currency)})` : "A complete estimate and range are not available."],
      ].map(([title, value]) => <div key={title} className="min-w-0 space-y-1"><dt className="text-small text-muted-foreground">{title}</dt><dd className="text-base font-medium tabular-nums wrap-anywhere">{value}</dd></div>)}</dl>
    </div>
    {analysis.warnings.length > 0 && <div className="space-y-3"><h3 className="font-semibold">Warnings and limitations</h3>{analysis.warnings.map((warning, index) => <Alert key={`${warning.code}-${index}`} variant="warning">{warning.message}</Alert>)}</div>}
    <div className="max-w-prose space-y-6">{[["Summary", result.summary], ["Why Kairo reached this result", result.explanation], ...(!result.conclusive ? [["Why this is inconclusive", result.inconclusiveReason ?? result.reason ?? "The available data does not support a conclusive effect."]] : []), ["Recommended next step", result.nextAdvice ?? "No next-step advice was returned. Review the coverage and comparison before deciding what to test next."]].map(([title, text]) => <section key={title} className="space-y-2"><h3 className="font-semibold">{title}</h3><p className="text-small whitespace-pre-wrap wrap-anywhere text-muted-foreground">{text || "Not supplied for this result."}</p></section>)}</div>
    <AnalysisChart days={result.daily} start={analysis.start} currency={result.currency} metric={label(analysis.source.metric)} />
    <div className="space-y-3 rounded-lg border border-border bg-card p-5"><h3 className="font-semibold">Save or share this result</h3><p className="text-small text-muted-foreground">Workspace links require sign-in and access to this workspace. CSV contains the displayed daily data; values use the outcome and units shown above. It does not include confidence bounds for individual days.</p><div className="flex flex-wrap gap-3"><Button className={actionClass} variant="secondary" pending={copying} onClick={copy}>Copy link</Button><Button className={actionClass} variant="secondary" disabled={!dailyRows(result.daily).length} onClick={() => { try { downloadFile(csv(result.daily), `kairo-impact-lab-${analysis.id}.csv`); setMessage("CSV download started."); } catch { setMessage("CSV download could not start. Try downloading again."); } }}>Download CSV</Button>{analysis.memoryEntryId && validId(analysis.memoryEntryId) ? <Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${workspace}/brand-kit/memory/${analysis.memoryEntryId}`}>View saved Brand Memory lesson</Link> : canEdit && canRemember(analysis) ? <Button className={actionClass} pending={pending} disabled={blocked} onClick={onRemember}>Save to Brand Memory</Button> : <p className="self-center text-small text-muted-foreground">{!canEdit ? "Only marketers and admins can save lessons." : "Only conclusive increases or decreases can be saved as lessons."}</p>}</div>
      {manual && <div className="space-y-1.5"><label htmlFor="lab-share-link" className="text-small font-medium">Workspace result link</label><input id="lab-share-link" className={control} readOnly value={manual} onFocus={event => event.target.select()} /></div>}
      {message && <p ref={notice} tabIndex={-1} role="status" className="text-small">{message}</p>}
    </div>
    <details className="text-small text-muted-foreground"><summary className="min-h-11 cursor-pointer py-3 text-primary">Analysis details</summary><dl className="space-y-2 wrap-anywhere">{[["Method", result.method], ["Model", result.model], ["Prompt version", result.promptVersion], ["Finished", result.finishedAt]].map(([key, value]) => <div key={key}><dt className="font-medium">{key}</dt><dd>{value ?? "Not supplied"}</dd></div>)}</dl></details>
  </section>;
}
