"use client";

import Link from "next/link";
import { useActionState, useEffect, useRef, useState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle, AlertDialogTrigger } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { NativeSelect } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { idle } from "@/lib/forms";

import type { DecisionState } from "../actions";

type Action = (previous: DecisionState, data: FormData) => Promise<DecisionState>;

export function OpportunityDecisions({ status, available, canDecide, roomHref, accept, save, unsave, dismiss }: { status: string; available: boolean; canDecide: boolean; roomHref?: string; accept: Action; save: Action; unsave: Action; dismiss: Action }) {
  const [dismissed, setDismissed] = useState(false);
  const result = useRef<HTMLDivElement>(null);
  useEffect(() => { if (dismissed) result.current?.focus(); }, [dismissed]);
  if (dismissed) return <div ref={result} tabIndex={-1}><FormStatus state={{ status: "saved", message: "Opportunity dismissed. Your feedback will help rank future opportunities." }} /></div>;
  if (!["new", "saved"].includes(status)) return <Alert variant={status === "accepted" ? "success" : "neutral"}>This opportunity is {status}.{roomHref && <Link href={roomHref} className="mt-2 text-primary hover:underline">Open campaign room</Link>}</Alert>;
  if (!canDecide) return <Alert variant="info">Only workspace marketers can accept, save or dismiss opportunities.</Alert>;
  return <section aria-label="Opportunity actions" className="flex flex-col gap-4 rounded-lg border border-border bg-card p-4 md:p-5">
    {!available && <Alert variant="warning">None of the matching products is in stock. You can save this opportunity, but accepting needs a product in stock.</Alert>}
    <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap">
      <AcceptDialog action={accept} disabled={!available} />
      <SaveAction status={status} save={save} unsave={unsave} />
      <DismissDialog action={dismiss} onDismissed={() => setDismissed(true)} />
    </div>
  </section>;
}

function SaveAction({ status, save, unsave }: { status: string; save: Action; unsave: Action }) {
  const [saved, setSaved] = useState(status === "saved");
  const [state, submit, pending] = useActionState(async (previous: DecisionState, data: FormData) => {
    const next = await (saved ? unsave : save)(previous, data);
    if (next.status === "saved") setSaved(next.opportunityStatus === "saved");
    return next;
  }, idle);
  return <form action={submit} className="flex flex-col gap-2"><Button type="submit" variant="secondary" pending={pending} className="w-full">{saved ? "Remove from Saved" : "Save opportunity"}</Button><FormStatus state={state} /></form>;
}

function AcceptDialog({ action, disabled }: { action: Action; disabled: boolean }) {
  const [open, setOpen] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const [state, submit, pending] = useActionState(action, idle);
  return <AlertDialog open={open} onOpenChange={(value) => { if (!pending) setOpen(value); }}>
    <AlertDialogTrigger render={<Button disabled={disabled} className="w-full sm:w-auto" />}>Accept opportunity</AlertDialogTrigger>
    <AlertDialogContent initialFocus={cancel}>
      <AlertDialogTitle>Accept this opportunity?</AlertDialogTitle>
      <AlertDialogDescription>This opens a campaign room with this evidence attached. Accepting does not launch ads or spend money.</AlertDialogDescription>
      <form action={submit} className="flex flex-col gap-4"><FormStatus state={state} /><AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel><Button type="submit" pending={pending} disabled={disabled}>Accept opportunity</Button></AlertDialogFooter></form>
    </AlertDialogContent>
  </AlertDialog>;
}

function DismissDialog({ action, onDismissed }: { action: Action; onDismissed: () => void }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");
  const cancel = useRef<HTMLButtonElement>(null);
  const [state, submit, pending] = useActionState(async (previous: DecisionState, form: FormData) => {
    const next = await action(previous, form);
    if (next.status === "saved") { setOpen(false); onDismissed(); }
    return next;
  }, idle);
  return <AlertDialog open={open} onOpenChange={(value) => { if (!pending) setOpen(value); }}>
    <AlertDialogTrigger render={<Button variant="destructive" className="w-full sm:w-auto" />}>Dismiss opportunity</AlertDialogTrigger>
    <AlertDialogContent initialFocus={cancel}>
      <AlertDialogTitle>Dismiss this opportunity?</AlertDialogTitle>
      <AlertDialogDescription>It leaves your open list. Your reason helps Kairo rank future opportunities.</AlertDialogDescription>
      <form action={submit} onReset={(event) => event.preventDefault()} className="flex flex-col gap-4">
        <div role={state.fieldErrors ? "alert" : undefined} className="flex flex-col gap-4">
          <Field><label htmlFor="dismiss-reason">Reason</label><NativeSelect id="dismiss-reason" name="reason" value={reason} onChange={(event) => setReason(event.target.value)} required aria-invalid={!!state.fieldErrors?.reason} aria-describedby={state.fieldErrors?.reason ? "dismiss-reason-error" : undefined}>
            <option value="">Choose a reason</option><option value="not_relevant">Not relevant</option><option value="too_small">Too small</option><option value="wrong_timing">Wrong timing</option><option value="already_covered">Already covered</option><option value="other">Other</option>
          </NativeSelect><FieldError id="dismiss-reason-error" errors={state.fieldErrors?.reason} /></Field>
          <Field><label htmlFor="dismiss-note">Note{reason === "other" ? "" : " (optional)"}</label><Textarea id="dismiss-note" name="note" value={note} onChange={(event) => setNote(event.target.value)} maxLength={500} required={reason === "other"} aria-invalid={!!state.fieldErrors?.note} aria-describedby={state.fieldErrors?.note ? "dismiss-note-error" : undefined} /><FieldError id="dismiss-note-error" errors={state.fieldErrors?.note} /></Field>
        </div>
        <FormStatus state={state} /><AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel><Button type="submit" variant="destructive-solid" pending={pending}>Dismiss opportunity</Button></AlertDialogFooter>
      </form>
    </AlertDialogContent>
  </AlertDialog>;
}
