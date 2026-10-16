"use client";

import { useEffect, useId, useRef, useState, useTransition } from "react";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import { dates } from "../../_lib/presentation";
import { decideRecommendation } from "../actions";
import { approverNames, changeRows, channelName, context, label, type CareState, type Member, type Recommendation, type Recommendations, type Settings } from "../_lib/model";
import { Evidence } from "./evidence";

export function RecommendationCard({ recommendation: rec, test, roomVersion, settings, members, currency, timeZone, brandId, canDismiss, onResult, busy, onBusy }: { recommendation: Recommendation; test: Recommendations["test"]; roomVersion: number | string; settings: Settings; members: Member[]; currency: string; timeZone: string; brandId: string; canDismiss: boolean; onResult: (state: CareState) => void; busy: boolean; onBusy: (busy: boolean) => void }) {
  const id = useId(), errorRef = useRef<HTMLDivElement>(null);
  const [opened, open] = useState<{ kind: "approve" | "dismiss"; context: string; version: number } | null>(null);
  const [reason, setReason] = useState("");
  const [state, setState] = useState<CareState>({ status: "idle" });
  const [pending, start] = useTransition();
  const key = context(rec, test, roomVersion), stale = opened !== null && opened.context !== key || state.conflict === true;
  const rows = changeRows(rec, currency), approvers = approverNames(rec, settings, members);
  const proposed = rec.state === "proposed";
  useEffect(() => { if (state.status === "error") errorRef.current?.focus(); }, [state]);
  const begin = (kind: "approve" | "dismiss") => { setState({ status: "idle" }); open({ kind, context: key, version: Number(rec.version) }); };
  const act = () => { if (!opened || stale || pending || busy) return; const snapshot = opened; onBusy(true); start(async () => { const result = await decideRecommendation(brandId, rec.id, snapshot.version, snapshot.kind === "approve" ? { kind: "approve" } : { kind: "dismiss", reason }); setState(result); onBusy(false); if (result.status === "saved") { open(null); setReason(""); onResult(result); } }); };
  return <article aria-labelledby={`${id}-title`} className="min-w-0 space-y-5 p-4 sm:p-6">
    <div className="flex flex-wrap items-center gap-2"><Badge variant="neutral">{rec.source === "google" ? "Google Ads" : "Kairo"}</Badge><Badge variant={rec.held ? "info" : rec.state === "applied" ? "success" : proposed ? "gate" : "neutral"}>{rec.held ? "Held during test" : label(rec.state)}</Badge><span className="text-small text-muted-foreground">{channelName(rec.campaignType)}</span></div>
    <h3 id={`${id}-title`} className="text-sm font-semibold wrap-anywhere">{rec.title}</h3>
    <div className="space-y-2 text-small"><p className="font-medium">Expected effect</p><p className="wrap-anywhere">{rec.expectedEffect}</p><p className="text-muted-foreground">This is an estimate, not a guarantee or measured lift.</p></div>
    <div className="space-y-2 rounded-lg bg-muted p-4 text-small"><p className="font-medium">Why</p><p className="wrap-anywhere">{rec.reasoning}</p></div>
    {rows.length > 0 && <dl className="space-y-3 text-small">{rows.map(row => <div key={row.label} className="grid gap-1 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]"><dt className="text-muted-foreground">{row.label}</dt><dd className="wrap-anywhere tabular-nums">{row.value}</dd></div>)}</dl>}
    <details><summary className="min-h-11 cursor-pointer py-3 text-small text-primary">Supporting data and proposed details</summary><div className="space-y-5 rounded-lg bg-muted p-4"><Evidence value={rec.source === "google" ? rec.evidence : rec.change} currency={currency} />{rec.source !== "google" && <Evidence value={rec.evidence} currency={currency} />}</div></details>
    {rec.problem && <Alert variant="warning"><strong>Needs attention</strong><p className="wrap-anywhere">{rec.problem}</p></Alert>}
    <p className="text-small wrap-anywhere text-muted-foreground">Approver: {approvers}</p>
    {rec.held && <Alert variant="info">Kairo’s budget and target changes are held while the test runs. An eligible marketer can end the test early below; that can weaken the measurement.</Alert>}
    {!proposed && <p className="text-small wrap-anywhere text-muted-foreground">{rec.decidedAt ? `Decision recorded ${dates(timeZone).format(new Date(rec.decidedAt))}. ` : ""}{rec.dismissReason && `Dismissal reason: ${rec.dismissReason}`}</p>}
    {proposed && !rec.held && !rec.canApprove && <p className="text-small text-muted-foreground">You cannot approve this recommendation. The assigned approver must decide.</p>}
    {proposed && <Sheet open={opened !== null} onOpenChange={value => { if (!value && !pending) open(null); }}><div className="flex flex-wrap gap-3">{rec.canApprove && !rec.held && <SheetTrigger disabled={busy} className="min-h-11 rounded-md border border-border-strong px-4 text-small font-medium hover:bg-accent" onClick={() => begin("approve")}>Review approval</SheetTrigger>}{canDismiss && <SheetTrigger disabled={busy} className="min-h-11 rounded-md border border-border-strong px-4 text-small font-medium hover:bg-accent" onClick={() => begin("dismiss")}>Dismiss recommendation</SheetTrigger>}</div>
      <SheetContent side="bottom" className="gap-5 p-5 pb-[max(1.25rem,env(safe-area-inset-bottom))] sm:p-6 sm:pb-[max(1.5rem,env(safe-area-inset-bottom))]">
        <SheetTitle className="pr-8 wrap-anywhere">{opened?.kind === "approve" ? "Approve and apply recommendation?" : "Dismiss recommendation?"}</SheetTitle><SheetDescription className="wrap-anywhere">{rec.title}</SheetDescription>
        {opened?.kind === "approve" ? <><Alert variant="warning"><strong>Applies immediately in Google Ads</strong><p>This single approval applies the saved change. It may change bids, delivery or spend. Review the proposed values and expected effect before approving.</p></Alert><dl className="space-y-2 text-small">{rows.map(row => <div key={row.label}><dt className="text-muted-foreground">{row.label}</dt><dd className="wrap-anywhere tabular-nums">{row.value}</dd></div>)}</dl><p className="text-small wrap-anywhere">{rec.expectedEffect}</p>{rec.source === "google" && <details><summary className="min-h-11 cursor-pointer py-3 text-small text-primary">Google Ads proposed details</summary><Evidence value={rec.evidence} currency={currency} /></details>}</> : <><p className="text-small text-muted-foreground">This proposal will be dismissed without applying its change.</p><label htmlFor={`${id}-reason`} className="text-small font-medium">Dismissal reason (optional)</label><Textarea id={`${id}-reason`} maxLength={500} value={reason} onChange={event => setReason(event.target.value)} disabled={pending} aria-invalid={Boolean(state.fieldErrors?.reason)} aria-describedby={state.fieldErrors?.reason ? `${id}-reason-error` : undefined} />{state.fieldErrors?.reason && <p id={`${id}-reason-error`} className="text-small text-destructive-strong">{state.fieldErrors.reason.join(" ")}</p>}</>}
        {stale && <Alert variant="warning">The recommendation or test changed, or the last outcome needs checking. Your reason is kept. Cancel, reload and review the latest version.</Alert>}
        {state.status === "error" && <div ref={errorRef} tabIndex={-1} role="alert" className="text-small text-destructive-strong">{state.message ?? "Check the highlighted fields and try again."}</div>}
        <div className="flex flex-col gap-3 sm:flex-row"><Button variant="secondary" disabled={pending} onClick={() => open(null)}>Cancel</Button><Button disabled={pending || busy || stale || (opened?.kind === "approve" ? !rec.canApprove || rec.held : !canDismiss)} onClick={act}>{pending ? "Saving…" : opened?.kind === "approve" ? "Approve and apply" : "Dismiss recommendation"}</Button></div>
      </SheetContent>
    </Sheet>}
  </article>;
}
