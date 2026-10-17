"use client";

import { startTransition, useActionState, useEffect, useId, useRef, useState, type ReactNode } from "react";

import { FieldError } from "@/components/field-error";
import { useAskEditGuard } from "@/components/ask-kairo/edit-guard";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { saveBrief } from "../actions";
import { money, type BriefDetail, type BriefState } from "../_lib/model";
import { Reason } from "./brief-document";

export function BriefEditor({ detail, brandId, campaignId, disabled, onDirty, onBusy }: { detail: BriefDetail; brandId: string; campaignId: string; disabled: boolean; onDirty: (dirty: boolean) => void; onBusy: (busy: boolean) => void }) {
  const [held, setBaseline] = useState(detail);
  const [drafting, setDrafting] = useState(false);
  const baseline = drafting || Number(held.campaignVersion) > Number(detail.campaignVersion) ? held : detail;
  const [revision, setRevision] = useState(0);
  const id = useId();
  const summary = useRef<HTMLDivElement>(null);
  const [state, action, pending] = useActionState<BriefState, FormData>(async (_, form) => {
    const result = await saveBrief(brandId, campaignId, form);
    if (result.detail) { setBaseline(result.detail); setDrafting(false); setRevision(value => value + 1); onDirty(false); }
    return result;
  }, { status: "idle" });
  useEffect(() => { onBusy(pending); return () => onBusy(false); }, [pending, onBusy]);
  useEffect(() => { if (state.status === "error") summary.current?.focus(); }, [state]);
  const brief = baseline.brief!;
  useAskEditGuard(drafting || pending);
  const stale = String(detail.campaignVersion) !== String(baseline.campaignVersion) || String(detail.brief?.version) !== String(brief.version);
  const errors = state.fieldErrors ?? {};
  function group(field: string, label: string, child: (props: { id: string; "aria-describedby": string; "aria-invalid": boolean }) => ReactNode, reason = field) {
    const fieldId = `${id}-${field}`;
    return <div className="space-y-2" key={field}><label htmlFor={fieldId} className="block text-small font-semibold">{label}</label>{child({ id: fieldId, "aria-describedby": `${fieldId}-error ${fieldId}-why`, "aria-invalid": !!errors[field]?.length })}<FieldError id={`${fieldId}-error`} errors={errors[field]} /><div id={`${fieldId}-why`}><Reason brief={brief} field={reason} /></div></div>;
  }
  function choices(field: string, label: string, options: { value: string; label: string; disabled?: boolean }[], selected: string[], reason = field) {
    const fieldId = `${id}-${field}`;
    return <fieldset id={fieldId} tabIndex={-1} className="min-w-0 space-y-2" key={field} aria-describedby={`${fieldId}-error ${fieldId}-why`} aria-invalid={!!errors[field]?.length}><legend className="mb-2 text-small font-semibold">{label}</legend><div className="max-h-60 space-y-1 overflow-y-auto rounded-md border p-2">{options.length ? options.map(option => <label key={option.value} className="flex min-h-11 cursor-pointer items-start gap-2 rounded px-2 py-2 hover:bg-accent"><input className="mt-1 size-4 shrink-0 accent-primary" type="checkbox" name={field} value={option.value} defaultChecked={selected.includes(option.value)} disabled={option.disabled} /><span className="min-w-0 wrap-anywhere text-small">{option.label}</span></label>) : <p className="p-2 text-small text-muted-foreground">No choices available yet.</p>}</div><FieldError id={`${fieldId}-error`} errors={errors[field]} /><div id={`${fieldId}-why`}><Reason brief={brief} field={reason} /></div></fieldset>;
  }
  return <form action={action} onSubmit={event => { event.preventDefault(); const form = new FormData(event.currentTarget); startTransition(() => action(form)); }} onChange={() => { setBaseline(baseline); setDrafting(true); onDirty(true); }} className="space-y-6" noValidate>
    {stale && <Alert variant="warning"><p>A newer campaign version is available. Your edits are kept here. Reload it before saving.</p><Button type="button" variant="secondary" className="mt-2 self-start whitespace-normal" disabled={pending} onClick={() => { setBaseline(detail); setDrafting(false); setRevision(value => value + 1); onDirty(false); }}>Discard edits and load latest</Button></Alert>}
    {state.status === "error" && <div ref={summary} tabIndex={-1} className="space-y-2 rounded-md border border-destructive/25 p-3"><FormStatus state={state} />{Object.keys(errors).length > 0 && <ul className="list-inside list-disc text-small">{Object.entries(errors).map(([field, messages]) => <li key={field}><a className="text-primary underline" href={`#${id}-${field}`}>{messages.join(" ")}</a></li>)}</ul>}</div>}
    <fieldset key={`${revision}-${baseline.campaignVersion}-${brief.version}`} disabled={pending || disabled || stale} className="min-w-0 space-y-6">
      <legend className="sr-only">Edit brief fields</legend>
      <input type="hidden" name="version" value={String(baseline.campaignVersion)} /><input type="hidden" name="briefVersion" value={String(brief.version)} />
      {group("title", "Title", props => <Input {...props} name="title" defaultValue={brief.title} maxLength={120} />)}
      {group("objective", "Objective", props => <Textarea {...props} name="objective" defaultValue={brief.objective} maxLength={300} rows={3} />)}
      {group("kpi", "KPI", props => <select {...props} name="kpi" defaultValue={brief.kpi} className="h-11 w-full rounded-md border bg-card px-3 text-small"><option value="">Choose a KPI</option>{baseline.options.kpis.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}</select>)}
      {choices("campaignTypes", "Campaign types", baseline.options.campaignTypes, brief.campaignTypes)}
      {group("budget", `Budget (${brief.budget?.currency ?? baseline.budgetLimit?.currency ?? "workspace currency"})`, props => <><Input {...props} name="budget" inputMode="decimal" defaultValue={brief.budget?.amount ?? ""} /><p className="text-small text-muted-foreground">Per-campaign limit: {baseline.budgetLimit ? money(baseline.budgetLimit.amount, baseline.budgetLimit.currency) : "No limit set"}. Budgets above the limit go to a budget owner.</p></>)}
      <div className="grid gap-4 md:grid-cols-2">{group("startDate", "Start date", props => <Input {...props} type="date" name="startDate" defaultValue={brief.startDate ?? ""} />, "dates")}{group("endDate", "End date", props => <Input {...props} type="date" name="endDate" defaultValue={brief.endDate ?? ""} />, "dates")}</div>
      {choices("regions", "Regions", baseline.options.regions.map(region => ({ value: region.name, label: region.name })), brief.regions)}
      {group("audiences", "Audiences · one per line", props => <Textarea {...props} name="audiences" defaultValue={brief.audiences.join("\n")} rows={3} />)}
      {group("keyMessages", "Key messages · one per line", props => <Textarea {...props} name="keyMessages" defaultValue={brief.keyMessages.join("\n")} rows={3} />)}
      {choices("productIds", "Products", baseline.options.products.map(product => ({ value: product.id, label: `${product.title}${product.inStock ? "" : " · out of stock"}`, disabled: !product.inStock })), brief.products.filter(product => product.inStock).map(product => product.id), "products")}
      {group("landingPages", "Landing pages · one HTTPS address per line", props => <Textarea {...props} name="landingPages" defaultValue={brief.landingPages.join("\n")} rows={3} />)}
      {choices("languages", "Languages", baseline.options.languages, brief.languages)}
      <div className="flex flex-col gap-2 sm:flex-row"><Button type="submit" pending={pending} className="w-full sm:w-auto">Save brief</Button><Button type="button" variant="secondary" onClick={() => { setDrafting(false); setBaseline(detail); setRevision(value => value + 1); onDirty(false); }}>Discard edits</Button></div>
    </fieldset>
    {state.status === "saved" && <FormStatus state={state} />}
  </form>;
}
