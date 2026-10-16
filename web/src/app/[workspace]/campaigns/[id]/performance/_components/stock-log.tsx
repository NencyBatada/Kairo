"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { dates } from "../../_lib/presentation";
import { changeStock } from "../actions";
import { batchContext, channelName, pauseName, type Pause, type StockLog as Log, type StockState } from "../_lib/model";

export function StockLog({ log, brandId, campaignId, canUndo, admin, timeZone }: { log: Log; brandId: string; campaignId: string; canUndo: boolean; admin: boolean; timeZone: string }) {
  const [saved, save] = useState<{ base: string; value: Log } | null>(null);
  const current = saved?.base === JSON.stringify(log) ? saved.value : log;
  const [result, setResult] = useState<StockState>({ status: "idle" });
  const [busy, onBusy] = useState(false);
  const resultRef = useRef<HTMLDivElement>(null);
  const receive = (state: StockState) => { setResult(state); if (state.log) save({ base: JSON.stringify(log), value: state.log }); };
  useEffect(() => { if (result.status !== "idle") resultRef.current?.focus(); }, [result]);
  const batches = [...new Set(current.pauses.filter(pause => pause.state === "held" && pause.batchId).map(pause => pause.batchId!))];
  return <section aria-labelledby="stock-title" className="space-y-5">
    <div className="flex flex-wrap items-center justify-between gap-3"><h2 id="stock-title" className="text-base font-semibold">Stock pause log</h2><Button variant="secondary" disabled={busy} onClick={() => window.location.reload()}>Reload performance</Button></div><div><p className="text-small text-muted-foreground">Search and Demand Gen ads pause when a fresh feed reports no stock, then resume on restock. Shopping availability follows the Merchant Center feed.</p></div>
    <Alert variant={current.feedStale ? "warning" : "neutral"}><strong>{current.feedStale ? "Stock feed is stale" : current.feedUpdatedAt ? "Last stock feed" : "No stock feed recorded"}</strong><p>{current.feedUpdatedAt ? dates(timeZone).format(new Date(current.feedUpdatedAt)) : "No stock update has been recorded by the pause service yet."}{current.feedStale && " Automatic stock changes are held until a fresh feed arrives. Check the catalogue feed."}</p></Alert>
    {result.status !== "idle" && <div ref={resultRef} tabIndex={-1} role={result.status === "error" ? "alert" : "status"} className="rounded-lg border border-border bg-muted p-4 text-small">{result.message}</div>}
    {batches.map(id => <HeldBatch key={id} id={id} log={current} admin={admin} brandId={brandId} campaignId={campaignId} onResult={receive} busy={busy} onBusy={onBusy} />)}
    {!current.pauses.length ? <p className="rounded-lg border border-border bg-card p-5 text-small text-muted-foreground">No stock pauses recorded for this campaign.</p> : <ul className="divide-y divide-border rounded-xl border border-border bg-card">{current.pauses.map(pause => <li key={pause.id} className="space-y-3 p-4 sm:p-5"><div className="flex flex-wrap items-center justify-between gap-3"><h3 className="text-sm font-semibold">{channelName(pause.campaignType)} ad</h3><Badge variant={pause.state === "held" || pause.state === "paused" ? "warning" : "neutral"}>{pauseName(pause.state)}</Badge></div><p className="text-small wrap-anywhere text-muted-foreground">Products: {pause.productIds.join(", ") || "Not supplied"}</p><p className="font-mono text-xs wrap-anywhere">{pause.adResource}</p><details><summary className="min-h-11 cursor-pointer py-3 text-small text-primary">Pause history and Google Ads IDs</summary><dl className="space-y-2 text-small">{[["Campaign", pause.campaignResource], ["Recorded", pause.createdAt], ["Paused", pause.pausedAt], ["Resumed", pause.resumedAt], ["Decided", pause.decidedAt], ["Decided by", pause.decidedBy]].map(([label, value]) => value && <div key={label} className="grid gap-1 sm:grid-cols-[8rem_minmax(0,1fr)]"><dt className="text-muted-foreground">{label}</dt><dd className="wrap-anywhere">{["Recorded", "Paused", "Resumed", "Decided"].includes(label!) ? dates(timeZone).format(new Date(value)) : <span className="font-mono text-xs">{value}</span>}</dd></div>)}</dl></details>{pause.state === "paused" && pause.canUndo && (canUndo ? <UndoPause key={pause.id} pause={pause} brandId={brandId} campaignId={campaignId} onResult={receive} busy={busy} onBusy={onBusy} /> : <p className="text-small text-muted-foreground">A marketer or admin can undo this pause.</p>)}</li>)}</ul>}
  </section>;
}

function UndoPause({ pause, brandId, campaignId, onResult, busy, onBusy }: { pause: Pause; brandId: string; campaignId: string; onResult: (state: StockState) => void; busy: boolean; onBusy: (value: boolean) => void }) {
  const [pending, start] = useTransition();
  const [blocked, block] = useState<string | null>(null);
  const context = `${pause.id}:${pause.version}`;
  return <div className="space-y-2"><p className="text-small text-muted-foreground">Undo enables this ad immediately. It can spend again even while the product is out of stock.</p><Button variant="secondary" disabled={pending || busy || blocked === context} onClick={() => { if (busy) return; onBusy(true); start(async () => { const result = await changeStock(brandId, campaignId, { kind: "undo", id: pause.id, version: Number(pause.version) }); if (result.status === "error") block(context); onBusy(false); onResult(result); }); }}>{pending ? "Undoing pause…" : "Undo pause"}</Button>{blocked === context && <p className="text-small text-muted-foreground">Reload the log to check Google Ads before retrying.</p>}</div>;
}

function HeldBatch({ id, log, admin, brandId, campaignId, onResult, busy, onBusy }: { id: string; log: Log; admin: boolean; brandId: string; campaignId: string; onResult: (state: StockState) => void; busy: boolean; onBusy: (value: boolean) => void }) {
  const [decision, decide] = useState<{ kind: "confirm" | "dismiss"; context: string } | null>(null);
  const [state, setState] = useState<StockState>({ status: "idle" });
  const [pending, start] = useTransition();
  const errorRef = useRef<HTMLDivElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const context = batchContext(log, id);
  const stale = decision !== null && decision.context !== context || state.conflict === true;
  const held = log.pauses.filter(pause => pause.batchId === id && pause.state === "held");
  useEffect(() => { if (state.status === "error") errorRef.current?.focus(); }, [state]);
  return <Alert variant="warning"><strong>{held.length} {held.length === 1 ? "pause held" : "pauses held"} for admin review</strong><p>More than 30% of this campaign’s ads would pause together. Review the products and feed before deciding. Confirming pauses stops delivery for these ads; dismissing keeps them enabled.</p><ul className="my-2 space-y-2 text-small wrap-anywhere">{held.map(pause => <li key={pause.id}>{channelName(pause.campaignType)} · {pause.productIds.join(", ")}<span className="block font-mono text-xs">{pause.adResource}</span></li>)}</ul>{!admin ? <p>An admin must decide this batch.</p> : <div className="mt-2 flex flex-wrap gap-3"><Button variant="secondary" disabled={busy} onClick={() => { setState({ status: "idle" }); decide({ kind: "confirm", context }); }}>Review held pauses</Button><Button variant="secondary" disabled={busy} onClick={() => { setState({ status: "idle" }); decide({ kind: "dismiss", context }); }}>Keep ads enabled</Button></div>}
    <AlertDialog open={decision !== null} onOpenChange={open => { if (!open && !pending) decide(null); }}><AlertDialogContent initialFocus={cancelRef}>
      <AlertDialogHeader><AlertDialogTitle>{decision?.kind === "confirm" ? `Pause ${held.length} ads?` : "Keep the held ads enabled?"}</AlertDialogTitle><AlertDialogDescription>{decision?.kind === "confirm" ? "The listed ads stop serving in Google Ads. Each pause can be undone later." : "Dismiss this batch. These ads can keep serving and spending even while their products are out of stock."}</AlertDialogDescription></AlertDialogHeader>
      {stale && <Alert variant="warning">The feed or pauses changed. Cancel, reload and review the batch again.</Alert>}
      {state.status === "error" && <div ref={errorRef} tabIndex={-1} role="alert" className="text-small text-destructive-strong">{state.message ?? "The decision could not be saved. Reload and try again."}</div>}
      <AlertDialogFooter><AlertDialogCancel ref={cancelRef} disabled={pending}>Cancel</AlertDialogCancel><Button variant="destructive-solid" disabled={pending || busy || stale || !admin} onClick={() => { if (!decision || busy) return; const opened = decision; onBusy(true); start(async () => { const result = await changeStock(brandId, campaignId, { kind: opened.kind, id, context: opened.context }); setState(result); onBusy(false); if (result.status === "saved") { decide(null); onResult(result); } }); }}>{pending ? "Saving…" : decision?.kind === "confirm" ? "Pause listed ads" : "Keep ads enabled"}</Button></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </Alert>;
}
