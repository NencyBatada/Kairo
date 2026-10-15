"use client";

import { useActionState, useRef, useState } from "react";
import { FormStatus } from "@/components/form-status";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle, AlertDialogTrigger } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { idle, type FormState } from "@/lib/forms";

type Action = (previous: FormState, form: FormData) => Promise<FormState>;

export function RuleControl({ label, action }: { label: string; action: Action }) {
  const [state, submit, pending] = useActionState(action, idle);
  return <form action={submit} className="flex flex-col items-start gap-2"><Button type="submit" variant="secondary" pending={pending}>{label}</Button><FormStatus state={state} /></form>;
}

export function DeleteRule({ name, action }: { name: string; action: Action }) {
  const [open, setOpen] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const [state, submit, pending] = useActionState(async (previous: FormState, form: FormData) => {
    const next = await action(previous, form);
    if (next.status === "saved") setOpen(false);
    return next;
  }, idle);
  if (state.status === "saved") return <FormStatus state={state} />;
  return <AlertDialog open={open} onOpenChange={(value) => { if (!pending) setOpen(value); }}>
    <AlertDialogTrigger render={<Button variant="destructive" />}>Delete rule</AlertDialogTrigger>
    <AlertDialogContent initialFocus={cancel}><AlertDialogTitle>Delete {name}?</AlertDialogTitle><AlertDialogDescription>This rule stops checking for new opportunities. Existing opportunities and campaigns are kept.</AlertDialogDescription>
      <form action={submit} className="flex flex-col gap-4"><FormStatus state={state} /><AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel><Button type="submit" variant="destructive-solid" pending={pending}>Delete rule</Button></AlertDialogFooter></form>
    </AlertDialogContent>
  </AlertDialog>;
}
