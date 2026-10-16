"use client";

import { startTransition, useActionState, useEffect, useId, useRef, useState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { applyBrief, proposeBrief } from "../actions";
import type { BriefDetail, BriefState, Proposal } from "../_lib/model";
import { BriefDocument, BriefWarnings } from "./brief-document";

export function BriefInstructions({ detail, brandId, campaignId, disabled, now, onApplied, onBusy }: { detail: BriefDetail; brandId: string; campaignId: string; disabled: boolean; now: number; onApplied: (detail: BriefDetail) => void; onBusy: (busy: boolean) => void }) {
  const id = useId();
  const statusRef = useRef<HTMLDivElement>(null);
  const [clock, setClock] = useState(now);
  const [preview, setPreview] = useState<{ proposal: Proposal; version: string } | null>(null);
  const [applying, setApplying] = useState(false);
  const [applyState, setApplyState] = useState<BriefState>({ status: "idle" });
  const [state, action, pending] = useActionState<BriefState, FormData>(async (_, form) => {
    setApplyState({ status: "idle" });
    setPreview(null);
    const result = await proposeBrief(brandId, campaignId, form);
    if (result.proposal) setPreview({ proposal: result.proposal, version: String(form.get("version")) });
    return result;
  }, { status: "idle" });
  useEffect(() => { onBusy(pending || applying || !!preview); return () => onBusy(false); }, [pending, applying, preview, onBusy]);
  useEffect(() => { if (state.status === "error" || applyState.status !== "idle" || preview) statusRef.current?.focus(); }, [state, applyState, preview]);
  useEffect(() => { if (!preview) return; const timer = window.setInterval(() => setClock(Date.now()), 1000); return () => window.clearInterval(timer); }, [preview]);
  const stale = preview && (preview.version !== String(detail.campaignVersion) || String(preview.proposal.briefVersion) !== String(detail.brief?.version));
  const expired = preview && Date.parse(preview.proposal.expiresAt) <= clock;
  async function apply() {
    if (!preview || applying || disabled || stale || expired) return;
    setApplying(true);
    const result = await applyBrief(brandId, campaignId, preview.proposal.id, preview.version);
    setApplyState(result);
    if (result.detail) { setPreview(null); onApplied(result.detail); }
    setApplying(false);
  }
  return <section aria-labelledby={`${id}-title`} className="space-y-4 rounded-lg border bg-card p-5 sm:p-6">
    <div className="space-y-1"><h2 id={`${id}-title`} className="text-xl font-semibold">Change it in your own words</h2><p className="text-small text-muted-foreground">Instructions use the saved brief. Save direct edits first, then review the proposed changes before applying them.</p></div>
    <form action={action} onSubmit={event => { event.preventDefault(); const form = new FormData(event.currentTarget); startTransition(() => action(form)); }} className="space-y-3">
      <input type="hidden" name="version" value={String(detail.campaignVersion)} /><input type="hidden" name="briefVersion" value={String(detail.brief!.version)} />
      <label htmlFor={`${id}-instruction`} className="block text-small font-semibold">What should change?</label>
      <Textarea id={`${id}-instruction`} name="instruction" maxLength={500} rows={3} placeholder="Add Hindi and lead with quick-dry." disabled={disabled || pending || applying} aria-invalid={!!state.fieldErrors?.instruction} aria-describedby={`${id}-instruction-error`} />
      <FieldError id={`${id}-instruction-error`} errors={state.fieldErrors?.instruction} />
      <Button variant="secondary" type="submit" disabled={disabled || applying} pending={pending} className="w-full sm:w-auto">Preview changes</Button>
    </form>
    <div ref={statusRef} tabIndex={-1} className="space-y-3">
      {(preview || state.status === "error") && <FormStatus state={state} />}
      <FormStatus state={applyState} />
      {preview && <div className="space-y-4 border-t pt-4">
        <h3 className="font-semibold">Proposed changes</h3><p className="wrap-anywhere text-small">{preview.proposal.summary}</p>
        {(stale || expired) && <Alert variant="warning"><p>{expired ? "This preview has expired." : "The campaign changed after this preview was prepared."} Discard it and request a new preview.</p></Alert>}
        <dl className="divide-y rounded-md border px-4">{preview.proposal.changes.map((change, index) => <div key={index} className="space-y-2 py-4 wrap-anywhere"><dt className="font-semibold">{change.label}</dt><dd className="space-y-2 text-small"><p><span className="text-muted-foreground">Before: </span>{change.before || "Not set"}</p><p><span className="text-muted-foreground">After: </span>{change.after || "Not set"}</p><p className="text-muted-foreground">{change.reason}</p></dd></div>)}</dl>
        <BriefWarnings warnings={preview.proposal.warnings} />
        <details className="rounded-md border p-3"><summary className="cursor-pointer text-small font-semibold">Read the complete proposed brief</summary><div className="mt-5"><BriefDocument detail={{ ...detail, brief: preview.proposal.preview }} /></div></details>
        <div className="flex flex-col gap-2 sm:flex-row"><Button type="button" onClick={apply} pending={applying} disabled={disabled || pending || !!stale || !!expired}>Apply changes</Button><Button type="button" variant="secondary" disabled={applying} onClick={() => { setPreview(null); setApplyState({ status: "idle" }); }}>Discard preview</Button></div>
      </div>}
    </div>
  </section>;
}
