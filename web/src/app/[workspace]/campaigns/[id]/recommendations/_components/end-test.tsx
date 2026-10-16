"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { endTest } from "../actions";
import type { CareState, Recommendations, Room } from "../_lib/model";

export function EndTest({ room, test, brandId, canEnd, onResult, busy, onBusy }: { room: Room; test: Recommendations["test"]; brandId: string; canEnd: boolean; onResult: (result: CareState) => void; busy: boolean; onBusy: (value: boolean) => void }) {
  const [opened, open] = useState<{ context: string; version: number } | null>(null);
  const [state, setState] = useState<CareState>({ status: "idle" });
  const [pending, start] = useTransition();
  const cancelRef = useRef<HTMLButtonElement>(null), errorRef = useRef<HTMLDivElement>(null);
  const context = JSON.stringify([room.version, room.stage, test, canEnd]);
  const stale = opened !== null && opened.context !== context || state.conflict === true;
  useEffect(() => { if (state.status === "error") errorRef.current?.focus(); }, [state]);
  if (!test.running && !opened) return null;
  return <section aria-labelledby="end-test-title" className="space-y-4 rounded-xl border border-border bg-card p-4 sm:p-6"><h3 id="end-test-title" className="text-sm font-semibold">End the test early</h3><p className="text-small text-muted-foreground">Ending early closes the measurement window and unlocks Kairo’s recommendations. A shorter test can produce an inconclusive result. Ads keep running on their existing schedule and budget.</p>{canEnd ? <Button variant="destructive" disabled={busy} onClick={() => { setState({ status: "idle" }); open({ context, version: Number(room.version) }); }}>End test early</Button> : <p className="text-small text-muted-foreground">Only a marketer can end a live campaign’s test early.</p>}
    <AlertDialog open={opened !== null} onOpenChange={value => { if (!value && !pending) open(null); }}><AlertDialogContent initialFocus={cancelRef}>
      <AlertDialogHeader><AlertDialogTitle>End the test for {room.title} early?</AlertDialogTitle><AlertDialogDescription>This closes the measurement window now. Less data may weaken the result. It cannot be reopened through this screen. Ads are not paused by ending the test and can keep spending. Recommendations still need a separate approval.</AlertDialogDescription></AlertDialogHeader>
      {stale && <Alert variant="warning">The test or campaign changed. Cancel, reload and review its current status.</Alert>}
      {state.status === "error" && <div role="alert" ref={errorRef} tabIndex={-1} className="text-small text-destructive-strong">{state.message ?? "The test could not be ended. Reload and try again."}</div>}
      <AlertDialogFooter><AlertDialogCancel ref={cancelRef} disabled={pending}>Cancel</AlertDialogCancel><Button variant="destructive-solid" disabled={pending || busy || stale || !canEnd} onClick={() => { if (!opened || busy) return; const version = opened.version; onBusy(true); start(async () => { const result = await endTest(brandId, room.id, version); setState(result); onBusy(false); if (result.status === "saved") { open(null); onResult(result); } }); }}>{pending ? "Ending test…" : "End test early"}</Button></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </section>;
}
