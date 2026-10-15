"use client";

import { useActionState, useRef, useState } from "react";

import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle, AlertDialogTrigger } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { idle } from "@/lib/forms";

import type { KitAction } from "./types";

export function KitConfirm({ label, title, description, action, revision, disabled, destructive = false }: {
  label: string; title: string; description: string; action: KitAction; revision?: number | string; disabled?: boolean; destructive?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [reviewRevision, setReviewRevision] = useState(revision);
  const changed = disabled || revision !== reviewRevision;
  const cancel = useRef<HTMLButtonElement>(null);
  const [state, submit, pending] = useActionState(async (previous: Parameters<KitAction>[0], data: FormData) => {
    const result = await action(previous, data);
    if (result.status === "saved") setOpen(false);
    return result;
  }, idle);
  return <div className="flex flex-col gap-2">
    <AlertDialog open={open} onOpenChange={(value) => { if (!pending) { if (value) setReviewRevision(revision); setOpen(value); } }}>
      <AlertDialogTrigger render={<Button variant={destructive ? "destructive" : "secondary"} disabled={disabled} />}>{label}</AlertDialogTrigger>
      <AlertDialogContent initialFocus={cancel}>
        <AlertDialogTitle>{title}</AlertDialogTitle>
        <AlertDialogDescription>{description}</AlertDialogDescription>
        <form action={submit} className="flex flex-col gap-4">
          {reviewRevision !== undefined && <input type="hidden" name="revision" value={reviewRevision} />}
          {changed && <Alert variant="warning" role="alert">This draft changed while the confirmation was open. Cancel and review the latest version.</Alert>}
          <FormStatus state={state} />
          <AlertDialogFooter>
            <AlertDialogCancel ref={cancel} disabled={pending}>Cancel</AlertDialogCancel>
            <Button type="submit" pending={pending} disabled={changed} variant={destructive ? "destructive-solid" : "default"}>{label}</Button>
          </AlertDialogFooter>
        </form>
      </AlertDialogContent>
    </AlertDialog>
    {!open && state.status === "saved" && <FormStatus state={state} />}
  </div>;
}
