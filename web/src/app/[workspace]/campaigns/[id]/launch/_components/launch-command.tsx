"use client";

import { useId, useRef, useState } from "react";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import type { LaunchState } from "../_lib/model";

export function LaunchCommand({ label, description, contextKey, disabled, comment = false, run }: { label: string; description: string; contextKey: string; disabled: boolean; comment?: boolean; run: (reason: string) => Promise<LaunchState> }) {
  const id = useId();
  const [opened, setOpened] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [pending, setPending] = useState(false);
  const [state, setState] = useState<LaunchState>({ status: "idle" });
  const [done, setDone] = useState<string | null>(null);
  const confirm = useRef<HTMLButtonElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const result = useRef<HTMLDivElement>(null);
  const error = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const stale = opened !== null && opened !== contextKey;
  return <div className="space-y-3"><Button ref={trigger} variant={comment ? "secondary" : "default"} disabled={disabled || done === contextKey} onClick={() => { setOpened(contextKey); setState({ status: "idle" }); }} className="max-sm:w-full">{label}</Button><div ref={result} tabIndex={-1} className="outline-none">{opened === null && <FormStatus state={state} />}</div>
    <Sheet open={opened !== null} onOpenChange={next => { if (!next && !pending) setOpened(null); }}><SheetContent side="bottom" initialFocus={comment ? input : confirm} finalFocus={state.status === "saved" ? result : trigger}><form noValidate className="space-y-5 p-5" onSubmit={async event => {
      event.preventDefault(); if (pending || disabled || stale || state.conflict) return;
      if (comment && reason.trim().replace(/\s+/gu, " ").length < 2) { setState({ status: "error", message: "Add a comment saying what needs to change.", fieldErrors: { reason: ["Use at least two characters."] } }); input.current?.focus(); return; }
      setPending(true); let response: LaunchState;
      try { response = await run(reason); } catch { response = { status: "error", message: "The action could not be recorded. Your comment is kept. Reload progress before retrying." }; }
      setPending(false); setState(response);
      if (response.status === "saved") { setDone(contextKey); setOpened(null); setReason(""); } else requestAnimationFrame(() => error.current?.focus());
    }}><div className="space-y-2 pr-10"><SheetTitle>{label}?</SheetTitle><SheetDescription>{description}</SheetDescription></div>{comment && <div className="space-y-2"><label htmlFor={id} className="text-small font-semibold">What needs to change</label><Textarea ref={input} id={id} required maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} disabled={pending} aria-invalid={!!state.fieldErrors?.reason} aria-describedby={`${id}-error`} /><p id={`${id}-error`} className="text-small text-destructive-strong">{state.fieldErrors?.reason?.join(" ")}</p></div>}{(stale || state.conflict) && <Alert variant="warning">Saved progress changed. Close this sheet, reload and review before continuing. Your comment is kept.</Alert>}<div tabIndex={-1} className="outline-none" ref={error}><FormStatus state={state} /></div><div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end"><Button variant="secondary" disabled={pending} onClick={() => setOpened(null)}>Cancel</Button><Button ref={confirm} type="submit" pending={pending} disabled={disabled || stale || !!state.conflict} className="max-sm:whitespace-normal">{label}</Button></div></form></SheetContent></Sheet>
  </div>;
}
