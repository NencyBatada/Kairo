"use client";

import { useState } from "react";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { amount, dataCurrency, date, numeric, percent, sameKeys, selectionErrors, type Analysis, type Selection } from "../_lib/model";
import { actionClass, control } from "./setup-form";

export function ComparisonPicker({ analysis, pending, blocked, onCheck, onRun, onDiscard, onDirty }: { analysis: Analysis; pending: boolean; blocked: boolean; onCheck: (selection: Selection) => void; onRun: () => void; onDiscard: () => void; onDirty: (dirty: boolean) => void }) {
  const [baseline, setBaseline] = useState(analysis), [affected, setAffected] = useState(analysis.affected), [comparison, setComparison] = useState(analysis.comparison), [query, setQuery] = useState("");
  const dirty = !sameKeys(affected, baseline.affected) || !sameKeys(comparison, baseline.comparison);
  if (Number(baseline.version) !== Number(analysis.version) && !dirty && !pending) { setBaseline(analysis); setAffected(analysis.affected); setComparison(analysis.comparison); }
  const stale = Number(baseline.version) !== Number(analysis.version), series = baseline.data?.series ?? [];
  const invalid = selectionErrors(affected, comparison, series.map(row => row.key));
  const checked = !!analysis.data && !dirty && !stale && analysis.affected.length > 0 && analysis.comparison.length > 0;
  const candidates = new Map(baseline.candidates.map(row => [row.key, row]));
  function toggle(key: string, kind: "affected" | "comparison") {
    const values = kind === "affected" ? affected : comparison, update = kind === "affected" ? setAffected : setComparison;
    const next = values.includes(key) ? values.filter(value => value !== key) : [...values, key];
    update(next);
    onDirty(!sameKeys(kind === "affected" ? next : affected, baseline.affected) || !sameKeys(kind === "comparison" ? next : comparison, baseline.comparison));
  }
  return <section className="min-w-0 space-y-6" aria-labelledby="lab-comparison-heading"><div className="max-w-prose space-y-2"><h2 id="lab-comparison-heading" className="text-base font-semibold">Choose a fair comparison</h2><p className="text-small text-muted-foreground">First choose the series affected by your change. Kairo can suggest comparisons that tracked them beforehand. Only include comparisons you know the change did not touch.</p></div>
    {stale && <Alert variant="warning"><p>This analysis changed while you were choosing series. Your selection is still here. Review the latest saved version before checking or running.</p><Button className={`${actionClass} mt-2 w-fit`} variant="secondary" disabled={pending || blocked} onClick={onDiscard}>Discard selection and use saved version</Button></Alert>}
    {baseline.data && <div className="space-y-1 rounded-md bg-muted p-4 text-small"><h3 className="font-semibold">Loaded data coverage</h3><p>{date(baseline.data.from)} to {date(baseline.data.to)} · {baseline.data.historyDays} days before the change · {baseline.data.changeDays} of {baseline.data.plannedDays} change days covered.</p></div>}
    {!analysis.data && <Alert variant="warning">The saved analysis has no loaded data. Review the Data step and load its source before choosing a comparison. Any older draft selection below remains available for reference until you discard it.</Alert>}
    <div className="max-w-100 space-y-1.5"><label className="text-small font-medium" htmlFor="lab-series-search">Find a series</label><Input className={control} id="lab-series-search" type="search" value={query} onChange={event => setQuery(event.target.value)} /></div>
    <p className="text-small text-muted-foreground">{series.length} available series · {affected.length} affected · {comparison.length} comparison. Sources return up to 200 non-zero series; select up to 50 in each group. Totals cover the full loaded window.</p>
    <fieldset disabled={pending || blocked || stale || !analysis.data} className="min-w-0"><legend className="sr-only">Affected and unaffected comparison series</legend><ul className="max-h-120 overflow-y-auto rounded-lg border border-border bg-card">{series.filter(row => row.name.toLowerCase().includes(query.toLowerCase())).map(row => {
      const candidate = candidates.get(row.key), fit = numeric(candidate?.fit);
      return <li key={row.key} className="grid min-w-0 gap-3 border-b border-border p-4 last:border-0 md:grid-cols-[minmax(0,1fr)_auto]"><div className="min-w-0 space-y-1"><p className="font-medium wrap-anywhere">{row.name}</p><p className="text-small text-muted-foreground">Total {amount(row.total, dataCurrency(baseline))}{candidate ? ` · ${candidate.suggested ? "Suggested comparison" : "Comparison candidate"} · fit ${fit === null ? "not available" : fit.toFixed(2)}` : ""}</p></div><div className="flex flex-wrap gap-x-5 gap-y-2"><label className="flex min-h-11 cursor-pointer items-center gap-2 text-small"><input className="size-4 accent-primary" type="checkbox" aria-label={`Affected: ${row.name}`} checked={affected.includes(row.key)} disabled={!affected.includes(row.key) && (comparison.includes(row.key) || affected.length >= 50)} onChange={() => toggle(row.key, "affected")} />Affected</label><label className="flex min-h-11 cursor-pointer items-center gap-2 text-small"><input className="size-4 accent-primary" type="checkbox" aria-label={`Comparison: ${row.name}`} checked={comparison.includes(row.key)} disabled={!comparison.includes(row.key) && (affected.includes(row.key) || comparison.length >= 50)} onChange={() => toggle(row.key, "comparison")} />Comparison</label></div></li>;
    })}</ul></fieldset>
    {query && !series.some(row => row.name.toLowerCase().includes(query.toLowerCase())) && <p className="text-small">No series match this search. Existing selections are kept.</p>}
    {invalid && <p className="text-small text-muted-foreground">{invalid}</p>}
    <div className="flex flex-wrap gap-3"><Button className={actionClass} variant={checked ? "secondary" : "default"} pending={pending} disabled={blocked || stale || !!selectionErrors(affected, null, series.map(row => row.key))} onClick={() => onCheck({ version: baseline.version, affected, comparison: null })}>Suggest comparisons</Button><Button className={actionClass} variant="secondary" pending={pending} disabled={blocked || stale || !!invalid} onClick={() => onCheck({ version: baseline.version, affected, comparison })}>Check selected comparison</Button></div>
    {dirty && <Alert variant="info">Your selection has changed. Check it again before running; the fit and warnings below describe the saved selection.</Alert>}
    {dirty && !stale && <Button className={actionClass} variant="secondary" disabled={pending || blocked} onClick={onDiscard}>Discard selection changes</Button>}
    <div className="space-y-3 rounded-lg bg-muted p-4"><h3 className="text-small font-semibold">Data check · version {baseline.version}</h3><p className="text-small">Comparison fit: {numeric(baseline.fit) === null ? "Not checked" : numeric(baseline.fit)!.toFixed(2)} · Smallest detectable effect: {numeric(baseline.detectable) === null ? "Not available" : percent(numeric(baseline.detectable)!)}</p><p className="text-small text-muted-foreground">A fit of 0.70 or more is preferable. A detectable effect describes sensitivity, not the effect of this change.</p>{baseline.warnings.length ? <ul className="space-y-2">{baseline.warnings.map((warning, index) => <li key={`${warning.code}-${index}`}><Alert variant="warning">{warning.message}</Alert></li>)}</ul> : <p className="text-small text-muted-foreground">{baseline.affected.length ? "No warnings were returned by the last comparison check." : "Choose affected series and check a comparison to see warnings."}</p>}</div>
    <Button className={`${actionClass} w-full sm:w-auto`} variant={checked ? "default" : "secondary"} disabled={pending || blocked || !checked} onClick={onRun}>Run analysis</Button>
  </section>;
}
