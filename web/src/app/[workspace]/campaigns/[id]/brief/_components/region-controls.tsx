"use client";

import { startTransition, useActionState, useContext, useEffect, useId, useRef, useState } from "react";
import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import type { FormState } from "@/lib/forms";
import { ranked } from "../_lib/map";
import { percent, type ProofResult } from "../_lib/model";
import { changeLockedPlan, chooseHoldouts } from "../proof-actions";
import { BriefInteraction } from "./brief-interaction";

export function RegionControls({ result, brandId, campaignId, campaignVersion, onReload }: { result: ProofResult; brandId: string; campaignId: string; campaignVersion: string; onReload: () => void }) {
  const { blocked, setProofBusy } = useContext(BriefInteraction);
  const id = useId();
  const [draft, setDraft] = useState<{ plan: NonNullable<ProofResult["plan"]>; version: string; selected: string[]; end: string } | null>(null);
  const baseline = draft ?? { plan: result.plan!, version: campaignVersion, selected: result.plan!.holdouts.map(region => region.id), end: result.plan!.window.end ?? "" };
  const selected = baseline.selected;
  const end = baseline.end;
  const setSelected = (values: string[]) => setDraft({ ...baseline, selected: values });
  const setEnd = (value: string) => setDraft({ ...baseline, end: value });
  const [reason, setReason] = useState("");
  const [open, setOpen] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const summary = useRef<HTMLDivElement>(null);
  const dialogSummary = useRef<HTMLDivElement>(null);
  const locked = !!baseline.plan.lockedAt;
  const candidates = ranked(locked ? [...baseline.plan.candidates, ...baseline.plan.testRegions, ...baseline.plan.holdouts] : baseline.plan.candidates);
  const previous = baseline.plan.holdouts.map(region => region.id);
  const changed = selected.length !== previous.length || selected.some(value => !previous.includes(value)) || end !== (baseline.plan.window.end ?? "");
  const stale = baseline.version !== campaignVersion || String(baseline.plan.version) !== String(result.plan!.version);
  const allowed = locked ? result.canChangeAfterLock : result.canChooseHoldouts;
  const [state, action, pending] = useActionState<FormState, FormData>(async (_, form) => {
    const next = await (locked ? changeLockedPlan : chooseHoldouts)(brandId, campaignId, form);
    if (next.status === "saved") setOpen(false);
    return next;
  }, { status: "idle" });
  const saved = state.status === "saved";
  useEffect(() => { setProofBusy(pending || (changed && !saved)); return () => setProofBusy(false); }, [changed, pending, saved, setProofBusy]);
  useEffect(() => { if (state.status === "error") (open ? dialogSummary : summary).current?.focus(); }, [state, open]);
  function submit() {
    if (!formRef.current || pending || stale || blocked || !allowed || result.updating || saved) return;
    const form = new FormData(formRef.current);
    if (locked) form.set("reason", reason);
    startTransition(() => action(form));
  }
  const errors = state.fieldErrors ?? {};
  return <div className="space-y-4 border-t pt-5">
    <div className="space-y-1"><h3 className="text-lg font-semibold">{locked ? "Change the locked test plan" : "Choose holdout regions"}</h3><p className="text-small text-muted-foreground">{locked ? "Changes create a flagged version with your reason. They are recorded here; changing Google Ads targeting comes with Launch." : "Candidates are ranked by historical match. Choose 1 to 10; Kairo recalculates the test and holdout cost before approval."}</p></div>
    {(stale || saved) && <Alert variant={saved ? "info" : "warning"}><p>{saved ? "Your request was recorded. Load the latest saved plan before making another change." : "The campaign or test plan changed. Your choices are kept here; load the latest plan before saving."}</p><Button type="button" variant="secondary" disabled={pending} className="mt-2 self-start whitespace-normal" onClick={onReload}>{saved ? "Load latest plan" : "Discard choices and load latest"}</Button></Alert>}
    {blocked && <p className="text-small text-warning-strong">Save or discard brief edits and finish any instruction preview before changing holdouts.</p>}
    {!allowed && <p className="text-small text-muted-foreground">{result.plan?.lockedAt ? "This plan cannot be changed with your current access or campaign status." : "Holdouts can change while the brief is a draft, before approval and while no agent is working."}</p>}
    <div ref={summary} tabIndex={-1} className="space-y-2"><FormStatus state={state} />{!open && Object.entries(errors).map(([field, messages]) => <p key={field} className="text-small text-destructive-strong"><a className="underline" href={`#${id}-${field}`}>{messages.join(" ")}</a></p>)}</div>
    <form ref={formRef} onSubmit={event => { event.preventDefault(); if (locked) setOpen(true); else submit(); }} className="space-y-4">
      <input type="hidden" name="version" value={baseline.version} />
      <fieldset id={`${id}-regionIds`} tabIndex={-1} disabled={pending || blocked || stale || saved || result.updating || !allowed} className="min-w-0 space-y-3" aria-describedby={`${id}-regions-error`}>
        <legend className="mb-2 text-small font-semibold">Ranked holdout candidates · {selected.length} selected</legend>
        <div className="max-h-96 overflow-y-auto rounded-md border p-2">{candidates.map((region, index) => <label key={region.id} className="flex min-h-11 cursor-pointer items-start gap-3 rounded px-2 py-3 hover:bg-accent"><input type="checkbox" className="mt-1 size-4 shrink-0 accent-primary" name="regionIds" value={region.id} checked={selected.includes(region.id)} onChange={event => setSelected(event.target.checked ? [...selected, region.id] : selected.filter(value => value !== region.id))} /><span className="min-w-0 space-y-1 wrap-anywhere text-small"><span className="block font-medium">{index + 1}. {region.name}{region.state ? ` · ${region.state}` : ""}</span><span className="block text-muted-foreground">Match: {percent(region.matchScore)} · {region.inArea ? "Inside campaign area" : "Outside campaign area"}{region.share != null ? ` · historical share ${percent(region.share)}` : ""}</span></span></label>)}{!candidates.length && <p className="p-3 text-small text-muted-foreground">No ranked candidates are available yet.</p>}</div>
        <FieldError id={`${id}-regions-error`} errors={errors.regionIds ?? errors.holdoutRegionIds} />
        {locked && <div className="space-y-2"><label className="block text-small font-semibold" htmlFor={`${id}-endDate`}>Test end date</label><Input id={`${id}-endDate`} name="endDate" type="date" value={end} onChange={event => setEnd(event.target.value)} aria-describedby={`${id}-endDate-error`} /><FieldError id={`${id}-endDate-error`} errors={errors.endDate} /></div>}
        <div className="flex flex-col gap-2 sm:flex-row"><Button type="submit" variant="secondary" disabled={!changed} pending={pending} className="whitespace-normal">{locked ? "Review test plan change" : "Recalculate with these holdouts"}</Button><Button type="button" variant="ghost" disabled={!changed} onClick={() => { setDraft(null); setReason(""); }}>Discard choices</Button></div>
      </fieldset>
    </form>
    <AlertDialog open={open} onOpenChange={value => { if (!pending) setOpen(value); }}>
      <AlertDialogContent initialFocus={cancel}><AlertDialogTitle>Change the locked test plan?</AlertDialogTitle><AlertDialogDescription>The new version and your reason are recorded and flagged in the impact report. Ending the test early can weaken its result. The map shows the saved plan until this succeeds.</AlertDialogDescription>
        <dl className="space-y-2 wrap-anywhere text-small"><div><dt className="font-semibold">New holdouts</dt><dd>{candidates.filter(region => selected.includes(region.id)).map(region => region.name).join(", ") || "None selected"}</dd></div><div><dt className="font-semibold">New end date</dt><dd>{end || "Unchanged"}</dd></div></dl>
        <div className="space-y-2"><label htmlFor={`${id}-reason`} className="text-small font-semibold">Why does the test plan need to change?</label><Textarea id={`${id}-reason`} value={reason} onChange={event => setReason(event.target.value)} maxLength={500} disabled={pending} aria-invalid={!!errors.reason} aria-describedby={`${id}-reason-error`} /><FieldError id={`${id}-reason-error`} errors={errors.reason} /></div>
        <div ref={dialogSummary} tabIndex={-1} className="space-y-2"><FormStatus state={state} />{Object.entries(errors).filter(([field]) => field !== "reason").map(([field, messages]) => <p key={field} className="text-small text-destructive-strong">{messages.join(" ")}</p>)}</div>
        <AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel><Button type="button" variant="destructive-solid" pending={pending} disabled={blocked || stale || !allowed || !changed || reason.trim().length < 2} onClick={submit}>Record test plan change</Button></AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>;
}
