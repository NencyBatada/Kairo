"use client";

import { useCallback, useState, type ReactNode } from "react";
import type { components } from "@/lib/core-api/schema";
import { Alert } from "@/components/ui/alert";
import { dates } from "../../_lib/presentation";
import type { BriefDetail, ProofResult } from "../_lib/model";
import { BriefApproval } from "./brief-approval";
import { BriefDocument, BriefWarnings, DraftLabel } from "./brief-document";
import { BriefEditor } from "./brief-editor";
import { BriefInstructions } from "./brief-instructions";
import { BriefInteraction } from "./brief-interaction";

export function BriefWorkspace({ detail, room, proof, brandId, canEdit, timeZone, now, children }: { detail: BriefDetail; room: components["schemas"]["CampaignDetail"]; proof: ProofResult; brandId: string; canEdit: boolean; timeZone: string; now: number; children: ReactNode }) {
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [instructing, setInstructing] = useState(false);
  const [proofBusy, setProofBusy] = useState(false);
  const [applied, setApplied] = useState<BriefDetail | null>(null);
  const [editorKey, setEditorKey] = useState(0);
  const current = applied && Number(applied.campaignVersion) > Number(detail.campaignVersion) ? applied : detail;
  const onApplied = useCallback((next: BriefDetail) => { setApplied(next); setEditorKey(value => value + 1); setDirty(false); }, []);
  const editable = canEdit && current.editable;
  return <div className="space-y-6">
    <nav aria-label="Brief sections" className="flex flex-wrap gap-x-5 gap-y-2 text-small"><a href="#brief-fields" className="text-primary underline underline-offset-4">Brief fields</a><a href="#proof-plan" className="text-primary underline underline-offset-4">Test plan and regions</a><a href="#brief-approval" className="text-primary underline underline-offset-4">Gate 1 approval</a></nav>
    <section id="brief-fields" aria-labelledby="brief-fields-title" className="space-y-5 rounded-lg border bg-card p-5 sm:p-6">
      <div className="space-y-3"><h2 id="brief-fields-title" className="text-xl font-semibold">The campaign brief</h2><DraftLabel brief={current.brief!} approved={room.stage !== "brief_draft" && room.stage !== "discarded"} /></div>
      <BriefWarnings warnings={current.warnings} />
      {dirty && <Alert variant="info"><p>You have unsaved edits. Instructions and approval use the saved brief.</p></Alert>}
      {canEdit && !current.editable && <Alert variant="info"><p>{room.pendingApproval ? "Approval is pending. The brief cannot change until a decision is recorded." : room.stage !== "brief_draft" ? "The brief is read-only at this campaign stage." : "Wait for the Plan agent to finish before changing the brief."}</p></Alert>}
      {!canEdit && <p className="text-small text-muted-foreground">You can review this brief. A marketer can edit it while it is a draft.</p>}
      {proofBusy && <p className="text-small text-warning-strong">Save or discard your holdout choices before editing the brief.</p>}
      {canEdit && (editable || dirty) ? <BriefEditor key={editorKey} detail={current} brandId={brandId} campaignId={room.id} disabled={!editable || instructing || proofBusy} onDirty={setDirty} onBusy={setSaving} /> : <BriefDocument detail={current} />}
    </section>
    {editable && <BriefInstructions detail={current} brandId={brandId} campaignId={room.id} disabled={dirty || saving || proofBusy} now={now} onApplied={onApplied} onBusy={setInstructing} />}
    <BriefInteraction value={{ blocked: dirty || saving || instructing, setProofBusy }}>{children}</BriefInteraction>
    <BriefApproval key={`${room.stage}-${room.pendingApproval?.requestedAt ?? "draft"}`} detail={current} room={room} proof={proof} brandId={brandId} disabled={dirty || saving || instructing || proofBusy || String(current.campaignVersion) !== String(room.version)} canRequest={canEdit} />
    <section aria-labelledby="brief-history-title" className="space-y-3"><h2 id="brief-history-title" className="text-lg font-semibold">Brief history</h2><ol className="divide-y rounded-lg border bg-card px-5">{current.history.map(entry => <li key={entry.version} className="space-y-1 py-4 text-small wrap-anywhere"><p className="font-medium">Version {entry.version} · {entry.by?.name ?? "Kairo"}</p><p className="text-muted-foreground"><time dateTime={entry.createdAt}>{dates(timeZone).format(new Date(entry.createdAt))}</time> · {({ drafted: "Drafted", edit: "Direct edit", instruction: "Instruction applied", starter: "Starting brief", trigger: "Trigger brief" } as Record<string, string>)[entry.origin] ?? "Brief saved"}</p>{entry.instruction && <p>{entry.instruction}</p>}</li>)}</ol></section>
  </div>;
}
