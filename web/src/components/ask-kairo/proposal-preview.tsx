"use client";

import { useEffect, useRef, useState } from "react";
import { ExternalLink, Sparkles } from "lucide-react";
import { useRouter } from "next/navigation";
import Image from "next/image";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { canApply, changedFields, previewUrl, proposalReady, targetLabels, targetPath, type Proposal } from "@/lib/ask-kairo/model";
import type { LiveWorkspace } from "@/components/workspace-live-provider";
import type { ConversationController } from "./use-conversations";

export function ProposalPreview({ proposal, conversationId, workspace, controller, editing }: { proposal: Proposal; conversationId: string; workspace: LiveWorkspace; controller: ConversationController; editing: boolean }) {
  const router = useRouter();
  const [now, setNow] = useState(() => Date.now()), [confirmation, setConfirmation] = useState<{ proposal: Proposal; decision: "confirm" | "reject" } | null>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  const stale = !!confirmation && JSON.stringify(confirmation.proposal) !== JSON.stringify(proposal);
  const ready = proposalReady(proposal, now), allowed = canApply(proposal.target, workspace.roles), path = targetPath(proposal.target, workspace.handle);
  const pending = ["queued", "generating"].includes(proposal.status), expired = ["ready", "queued", "generating"].includes(proposal.status) && Date.parse(proposal.expiresAt) <= now;
  const blocked = controller.deciding || controller.sending || !allowed || !ready || editing || stale;
  const status = expired ? "Expired" : ({ queued: "Queued", generating: "Generating preview", ready: "Ready to review", applied: "Applied", rejected: "Discarded", failed: "Preview failed" }[proposal.status] ?? proposal.status);
  return <section className="grid min-w-0 gap-3 rounded-lg border border-border bg-muted/40 p-3" aria-label={`${targetLabels[proposal.target.kind]} change preview`}>
    <div className="flex flex-wrap items-center gap-2"><Sparkles className="size-4 text-primary" aria-hidden="true" /><h4 className="text-sm font-semibold">{targetLabels[proposal.target.kind]} change</h4><Badge variant={proposal.status === "failed" ? "warning" : "neutral"}>{status}</Badge></div>
    {proposal.status !== "applied" && <p className="text-xs text-muted-foreground">Drafted by Kairo · Nothing changes until you confirm.</p>}
    {path && <a href={path} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-11 items-center gap-1 text-small text-primary underline underline-offset-4">Open target<span className="sr-only"> in a new tab</span><ExternalLink className="size-3.5" aria-hidden="true" /></a>}
    {pending && <p className="text-small text-muted-foreground">{proposal.status === "queued" ? "Waiting to generate this preview." : "Kairo is preparing the candidate."} Media generation uses model resources before confirmation.</p>}
    {proposal.before !== null && proposal.after !== null && <div className="grid min-w-0 gap-3">{changedFields(proposal.before, proposal.after).map((field, index) => <div key={index} className="grid min-w-0 gap-2"><h5 className="text-small font-semibold">{field.name}</h5>{[["Before", field.before], ["After", field.after]].map(([label, text]) => <div key={label} className={`min-w-0 rounded-md border p-3 ${label === "After" ? "border-input bg-primary-soft" : "border-border bg-card"}`}><p className="text-xs font-medium text-subtle-foreground">{label}</p><p className="mt-1 whitespace-pre-wrap text-small wrap-anywhere">{text}</p></div>)}</div>)}</div>}
    {proposal.files.map(file => { const url = previewUrl(file.url, workspace.id, conversationId, proposal.id); if (!url) return <p key={file.name} className="text-small text-muted-foreground">{file.name}: preview unavailable.</p>; return <div key={file.name} className="grid min-w-0 gap-2"><p className="text-small font-medium wrap-anywhere">{file.name === "candidate" ? "Candidate media" : file.name}</p>{["image/jpeg", "image/png"].includes(file.contentType) ? <a href={url} target="_blank" rel="noopener noreferrer" className="block rounded-md border border-border bg-lightbox"><Image unoptimized width={640} height={480} src={url} alt={`${targetLabels[proposal.target.kind]} candidate preview`} className="max-h-72 w-full rounded-md object-contain" /></a> : file.contentType === "video/mp4" ? <video src={url} controls preload="metadata" className="max-h-72 w-full rounded-md bg-lightbox"><track kind="captions" label="Captions not supplied" /></video> : file.contentType === "audio/mpeg" ? <audio src={url} controls preload="metadata" className="w-full" /> : <p className="text-small text-muted-foreground">This media format cannot be previewed.</p>}<a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-11 items-center text-small text-primary underline underline-offset-4">Open {file.name} in a new tab</a></div>; })}
    {!!proposal.warnings.length && <Alert variant="warning"><ul className="list-inside list-disc space-y-1">{proposal.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul></Alert>}
    {proposal.error && <Alert variant="warning">{proposal.error}</Alert>}
    {expired && <p className="text-small text-muted-foreground">This preview expired. Ask Kairo for a new preview before applying changes.</p>}
    {proposal.status === "failed" && <p className="text-small text-muted-foreground">Ask for a new preview to try again.</p>}
    {proposal.status === "ready" && !expired && <><p className="text-xs text-muted-foreground">Expires {new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: workspace.timeZone ?? "UTC" }).format(new Date(proposal.expiresAt))} ({workspace.timeZone ?? "UTC"}).</p>{!allowed && <p className="text-small text-muted-foreground">Your current roles cannot apply this type of change.</p>}{editing && <Alert variant="warning">Save or discard your page edits before applying a chat change. Your unsaved work is kept.</Alert>}<Button className="w-full whitespace-normal" disabled={blocked} onClick={() => setConfirmation({ proposal, decision: "confirm" })}>Review and apply</Button></>}
    {!["applied", "rejected"].includes(proposal.status) && <Button variant="secondary" className="w-full" disabled={controller.deciding || controller.sending} onClick={() => setConfirmation({ proposal, decision: "reject" })}>Discard preview</Button>}
    <AlertDialog open={confirmation !== null} onOpenChange={value => { if (!value && !controller.deciding) setConfirmation(null); }}><AlertDialogContent initialFocus={cancel}>
      <AlertDialogTitle>{confirmation?.decision === "confirm" ? `Apply this ${targetLabels[proposal.target.kind]} change?` : `Discard this ${targetLabels[proposal.target.kind]} preview?`}</AlertDialogTitle>
      <AlertDialogDescription>{confirmation?.decision === "confirm" ? "This applies exactly the saved candidate shown in the preview. It may replace the listed companion media. Normal checks and approval still apply." : "This candidate will not be applied. The current saved item stays unchanged."}</AlertDialogDescription>
      {confirmation?.decision === "confirm" && !!proposal.warnings.length && <ul className="list-inside list-disc space-y-2 text-small wrap-anywhere">{proposal.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul>}
      {stale && <Alert variant="warning">The preview changed. Cancel and review it again.</Alert>}
      {confirmation?.decision === "confirm" && (editing || !ready || !allowed) && <Alert variant="warning">This change is not currently available to apply. Cancel, resolve page edits or request a new preview.</Alert>}
      {controller.error && <p role="alert" className="text-small wrap-anywhere text-destructive-strong">{controller.error.message}</p>}
      <AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={controller.deciding}>Cancel</AlertDialogCancel><Button variant={confirmation?.decision === "reject" ? "destructive-solid" : "default"} pending={controller.deciding} disabled={stale || controller.sending || confirmation?.decision === "confirm" && blocked} onClick={async () => { if (!confirmation) return; const applied = await controller.decide(confirmation.proposal, confirmation.decision); if (applied) { const confirm = confirmation.decision === "confirm"; setConfirmation(null); if (confirm) router.refresh(); } }}>{confirmation?.decision === "confirm" ? "Apply change" : "Discard preview"}</Button></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </section>;
}
