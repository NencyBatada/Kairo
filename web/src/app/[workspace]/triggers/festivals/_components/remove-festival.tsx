"use client";

import { useActionState, useRef, useState } from "react";
import { FormStatus } from "@/components/form-status";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle, AlertDialogTrigger } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { idle, type FormState } from "@/lib/forms";

export function RemoveFestival({ name, action, inUse }: { name: string; inUse: boolean; action: (previous: FormState, form: FormData) => Promise<FormState> }) {
  const [open, setOpen] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const [state, submit, pending] = useActionState(async (previous: FormState, form: FormData) => {
    const next = await action(previous, form);
    if (next.status === "saved") setOpen(false);
    return next;
  }, idle);
  if (state.status === "saved") return <FormStatus state={state} />;
  return <div className="flex flex-col items-start gap-2">
    <AlertDialog open={open} onOpenChange={(value) => { if (!pending) setOpen(value); }}>
      <AlertDialogTrigger render={<Button variant="destructive" disabled={inUse} aria-describedby={inUse ? "event-in-use" : undefined} />}>Remove event</AlertDialogTrigger>
      <AlertDialogContent initialFocus={cancel}><AlertDialogTitle>Remove {name}?</AlertDialogTitle><AlertDialogDescription>This removes the event from your brand’s calendar. It will no longer be available for new trigger rules.</AlertDialogDescription><form action={submit} className="flex flex-col gap-4"><FormStatus state={state} /><AlertDialogFooter><AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel><Button type="submit" variant="destructive-solid" pending={pending}>Remove event</Button></AlertDialogFooter></form></AlertDialogContent>
    </AlertDialog>
    {inUse && <p id="event-in-use" className="text-small text-muted-foreground">Change or delete the trigger rules using this event before removing it.</p>}
  </div>;
}
