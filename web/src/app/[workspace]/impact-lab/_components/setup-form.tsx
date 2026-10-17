"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { catalog, grainNames, label, setupOf, sourceNames, type Setup, type LabState } from "../_lib/model";

export const control = "min-h-11 w-full min-w-0 rounded-md border border-input bg-card px-3 text-base";
export const actionClass = "h-auto min-h-11 max-w-full whitespace-normal px-4 py-2";

export function SetupForm({ initial, pending, blocked, state, onSave, onDirty, resetKey }: { initial?: Setup; pending: boolean; blocked?: boolean; state: LabState; onSave: (draft: Setup) => void; onDirty?: (dirty: boolean) => void; resetKey?: number }) {
  const [draft, setDraft] = useState(initial ?? setupOf()), [key, setKey] = useState(resetKey);
  if (key !== resetKey) { setKey(resetKey); setDraft(initial ?? setupOf()); }
  const source = draft.source!, errors = state.fieldErrors ?? {};
  function update(next: Setup) { setDraft(next); onDirty?.(JSON.stringify(next) !== JSON.stringify(initial)); }
  const sourceChange = (field: string, value: string) => {
    const next = { ...source, [field]: value };
    if (field === "kind") {
      next.grain = catalog[value] ? Object.keys(catalog[value])[0] : "series";
      next.metric = catalog[value]?.[next.grain]?.[0] ?? "";
      next.table = next.dateColumn = next.valueColumn = next.seriesColumn = null;
    }
    if (field === "grain") next.metric = catalog[source.kind!]?.[value]?.[0] ?? "";
    update({ ...draft, source: next });
  };
  const error = (name: string) => errors[name] && <p id={`lab-${name}-error`} className="text-small text-destructive-strong">{errors[name].join(" ")}</p>;
  const invalid = (name: string) => ({ "aria-invalid": !!errors[name], "aria-describedby": errors[name] ? `lab-${name}-error` : undefined });
  return <form noValidate className="max-w-160 space-y-6" onSubmit={event => { event.preventDefault(); if (!pending && !blocked) onSave(draft); }}>
    <fieldset disabled={pending} className="min-w-0 space-y-5"><legend className="mb-4 text-base font-semibold">Describe the change</legend>
      <div className="space-y-1.5"><Label htmlFor="lab-name">Analysis name</Label><Input className={control} id="lab-name" value={draft.name ?? ""} maxLength={200} {...invalid("name")} onChange={event => update({ ...draft, name: event.target.value })} />{error("name")}</div>
      <div className="space-y-1.5"><Label htmlFor="lab-whatChanged">What changed?</Label><p className="text-small text-muted-foreground">Describe the price cut, TV burst, store opening or other change you want to measure.</p><Textarea id="lab-whatChanged" value={draft.whatChanged ?? ""} maxLength={1000} rows={4} {...invalid("whatChanged")} onChange={event => update({ ...draft, whatChanged: event.target.value })} />{error("whatChanged")}</div>
      <div className="grid gap-5 md:grid-cols-2">{(["start", "end"] as const).map(field => <div key={field} className="min-w-0 space-y-1.5"><Label htmlFor={`lab-${field}`}>{field === "start" ? "Change started" : "Change ended or planned end"}</Label><Input id={`lab-${field}`} className={control} type="date" value={draft[field] ?? ""} {...invalid(field)} onInput={event => update({ ...draft, [field]: event.currentTarget.value })} onChange={event => update({ ...draft, [field]: event.target.value })} />{error(field)}</div>)}</div>
      <p className="text-small text-muted-foreground">Use dates in India. The change must have started before today; measure up to 180 days. An ongoing change can have a future end date.</p>
      <div className="space-y-1.5"><Label htmlFor="lab-affectsKind">What did the change affect?</Label><select id="lab-affectsKind" className={control} value={draft.affectsKind ?? "regions"} {...invalid("affectsKind")} onChange={event => update({ ...draft, affectsKind: event.target.value })}><option value="regions">Regions</option><option value="products">Products</option><option value="other">Other</option></select>{error("affectsKind")}</div>
    </fieldset>
    <fieldset disabled={pending} className="min-w-0 space-y-5"><legend className="mb-4 text-base font-semibold">Choose the outcome data</legend>
      <div className="space-y-1.5"><Label htmlFor="lab-source.kind">Source</Label><select id="lab-source.kind" className={control} value={source.kind ?? "google_ads"} {...invalid("source.kind")} onChange={event => sourceChange("kind", event.target.value)}>{Object.entries(sourceNames).map(([key, name]) => <option key={key} value={key}>{name}</option>)}</select>{error("source.kind")}</div>
      {catalog[source.kind!] ? <>
        <div className="space-y-1.5"><Label htmlFor="lab-source.grain">Break down by</Label><select id="lab-source.grain" className={control} value={source.grain ?? ""} {...invalid("source.grain")} onChange={event => sourceChange("grain", event.target.value)}>{Object.keys(catalog[source.kind!]).map(key => <option key={key} value={key}>{grainNames[key]}</option>)}</select>{error("source.grain")}</div>
        <div className="space-y-1.5"><Label htmlFor="lab-source.metric">Outcome</Label><select id="lab-source.metric" className={control} value={source.metric ?? ""} {...invalid("source.metric")} onChange={event => sourceChange("metric", event.target.value)}>{catalog[source.kind!][source.grain ?? ""]?.map(key => <option key={key} value={key}>{label(key)}</option>)}</select>{error("source.metric")}</div>
      </> : <div className="space-y-1.5"><Label htmlFor="lab-source.metric">Outcome name and unit</Label><p className="text-small text-muted-foreground">For example, “Units sold” or “Revenue in INR”. Custom sources do not supply a currency code.</p><Input id="lab-source.metric" className={control} maxLength={60} value={source.metric ?? ""} {...invalid("source.metric")} onChange={event => sourceChange("metric", event.target.value)} />{error("source.metric")}</div>}
      {source.kind === "bigquery" && <><p className="text-small text-muted-foreground">Use a connected BigQuery table with a date column, a non-negative numeric outcome and a series column identifying each region or product.</p>{(["table", "dateColumn", "valueColumn", "seriesColumn"] as const).map(field => <div key={field} className="space-y-1.5"><Label htmlFor={`lab-source.${field}`}>{{ table: "Table (project.dataset.table)", dateColumn: "Date column", valueColumn: "Outcome value column", seriesColumn: "Series column" }[field]}</Label><Input className={control} id={`lab-source.${field}`} value={source[field] ?? ""} {...invalid(`source.${field}`)} onChange={event => sourceChange(field, event.target.value)} />{error(`source.${field}`)}</div>)}</>}
      {source.kind === "file" && <p className="text-small text-muted-foreground">After saving, upload a CSV with date, series and value columns. Include affected and unaffected series, ideally with at least eight weeks before the change.</p>}
    </fieldset>
    <Button type="submit" className={`${actionClass} w-full sm:w-auto`} pending={pending} disabled={blocked}>Save setup and continue</Button>
  </form>;
}
