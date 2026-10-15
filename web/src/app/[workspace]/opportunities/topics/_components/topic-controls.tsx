"use client";

import { useActionState, useRef, useState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle, AlertDialogTrigger } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { idle, type FormState } from "@/lib/forms";

type Action = (previous: FormState, form: FormData) => Promise<FormState>;

export function AddTopic({ action }: { action: Action }) {
  const [state, submit, pending] = useActionState(action, idle);
  const [phrase, setPhrase] = useState("");
  const [kind, setKind] = useState("watch");
  return <form action={submit} onReset={(event) => event.preventDefault()} className="flex max-w-160 flex-col gap-5">
    <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_10rem]" role={state.fieldErrors ? "alert" : undefined}>
      <Field><label htmlFor="topic-phrase">Topic</label><Input id="topic-phrase" name="phrase" value={phrase} onChange={(event) => setPhrase(event.target.value)} minLength={2} maxLength={100} required placeholder="For example, waterproof trainers" aria-invalid={!!state.fieldErrors?.phrase} aria-describedby={state.fieldErrors?.phrase ? "topic-phrase-error" : undefined} /><FieldError id="topic-phrase-error" errors={state.fieldErrors?.phrase} /></Field>
      <Field><label htmlFor="topic-kind">Preference</label><NativeSelect id="topic-kind" name="kind" value={kind} onChange={(event) => setKind(event.target.value)} aria-invalid={!!state.fieldErrors?.kind} aria-describedby={state.fieldErrors?.kind ? "topic-kind-error" : undefined}><option value="watch">Watch</option><option value="ignore">Ignore</option></NativeSelect><FieldError id="topic-kind-error" errors={state.fieldErrors?.kind} /></Field>
    </div>
    <div className="flex flex-col items-start gap-3"><Button type="submit" pending={pending} className="w-full sm:w-auto">Add topic</Button><FormStatus state={state} /></div>
  </form>;
}

export function RemoveTopic({ phrase, kind, action }: { phrase: string; kind: string; action: Action }) {
  const [open, setOpen] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const [state, submit, pending] = useActionState(async (previous: FormState, form: FormData) => {
    const next = await action(previous, form);
    if (next.status === "saved") setOpen(false);
    return next;
  }, idle);
  if (state.status === "saved") return <FormStatus state={state} />;
  return <AlertDialog open={open} onOpenChange={(value) => { if (!pending) setOpen(value); }}>
    <AlertDialogTrigger render={<Button variant="destructive" aria-label={`Remove ${phrase}`} />}>Remove</AlertDialogTrigger>
    <AlertDialogContent initialFocus={cancel}>
      <AlertDialogTitle>Remove {phrase}?</AlertDialogTitle>
      <AlertDialogDescription>{kind === "ignore" ? "Matching opportunities can appear again if their score and stock qualify." : "Matching opportunities will follow the usual score cut-off again."}</AlertDialogDescription>
      <form action={submit} className="flex flex-col gap-4"><FormStatus state={state} /><AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel><Button type="submit" variant="destructive-solid" pending={pending}>Remove topic</Button></AlertDialogFooter></form>
    </AlertDialogContent>
  </AlertDialog>;
}
