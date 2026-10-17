"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Alert } from "@/components/ui/alert";
import { useAskEditGuard } from "@/components/ask-kairo/edit-guard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { editMemory, removeMemory } from "../actions";
import type { Detail, MemoryState } from "../_lib/model";

export function MemoryEditor({ detail, brandId, canEdit }: { detail: Detail; brandId: string; canEdit: boolean }) {
  const router = useRouter(), [saved, setSaved] = useState<Detail | null>(null);
  const current = saved && saved.entry.id === detail.entry.id && Number(saved.entry.version) > Number(detail.entry.version) ? saved : detail;
  const [baseline, setBaseline] = useState(detail.entry), [title, setTitle] = useState(detail.entry.title), [lesson, setLesson] = useState(detail.entry.lesson);
  const [state, setState] = useState<MemoryState>({ status: "idle" }), [removal, setRemoval] = useState<{ id: string; title: string; version: number } | null>(null);
  const [pending, start] = useTransition(), busy = useRef(false), cancelRef = useRef<HTMLButtonElement>(null), noticeRef = useRef<HTMLDivElement>(null), removeErrorRef = useRef<HTMLDivElement>(null), removeTrigger = useRef<HTMLButtonElement>(null);
  const stale = baseline.id !== current.entry.id || Number(baseline.version) !== Number(current.entry.version) || current.entry.state !== "active" || state.conflict === true;
  useAskEditGuard(title !== baseline.title || lesson !== baseline.lesson || pending || removal !== null);
  const removalStale = !!removal && (removal.id !== current.entry.id || removal.version !== Number(current.entry.version) || current.entry.state !== "active" || state.conflict === true);
  const removed = state.removed || current.entry.state === "removed";
  useEffect(() => { if (state.status !== "idle") (removal && state.status === "error" ? removeErrorRef : noticeRef).current?.focus(); }, [state, removal]);
  function useCurrent() { setBaseline(current.entry); setTitle(current.entry.title); setLesson(current.entry.lesson); setState({ status: "idle" }); }
  function perform(kind: "save" | "remove") {
    if (busy.current || !canEdit || removed || kind === "save" && stale || kind === "remove" && (!removal || removalStale)) return;
    busy.current = true;
    start(async () => {
      try {
        const result = kind === "save" ? await editMemory(brandId, baseline.id, Number(baseline.version), title, lesson) : await removeMemory(brandId, baseline.id, removal!.version);
        setState(result);
        if (result.detail) { setSaved(result.detail); setBaseline(result.detail.entry); setTitle(result.detail.entry.title); setLesson(result.detail.entry.lesson); }
        if (result.removed) setRemoval(null);
      } catch {
        setState({ status: "error", conflict: true, message: "The connection was interrupted. Your draft is still here. Reload and review the saved entry before trying again." });
      } finally { busy.current = false; }
    });
  }
  return <section aria-labelledby="edit-memory" className="space-y-4 rounded-lg border border-border bg-card p-4 md:p-5"><h2 id="edit-memory" className="text-base font-semibold">Edit this lesson</h2>
    {state.status !== "idle" && !removal && <div ref={noticeRef} tabIndex={-1} role={state.status === "error" ? "alert" : "status"} className="space-y-1 text-small wrap-anywhere"><p>{state.message ?? (state.fieldErrors ? "Check the fields below." : "The entry could not be saved.")}</p>{state.fieldErrors && Object.entries(state.fieldErrors).filter(([key]) => key !== "title" && key !== "lesson").map(([key, errors]) => <p key={key}>{errors.join(" ")}</p>)}</div>}
    {removed && (title !== baseline.title || lesson !== baseline.lesson) && <div className="space-y-2 rounded-md bg-muted p-4 text-small"><h3 className="font-semibold">Your unsaved draft</h3><p className="whitespace-pre-wrap wrap-anywhere">{title}</p><p className="whitespace-pre-wrap wrap-anywhere">{lesson}</p><p className="text-muted-foreground">You can copy this draft for reference. The removed entry cannot be edited.</p></div>}
    {removed ? <Alert variant="neutral">This entry is removed. It stays available for reference, and future agent work will not use it.</Alert> : !canEdit ? <p className="text-small text-muted-foreground">Only marketers and admins can edit or remove memory entries.</p> : <>
      {stale && <Alert variant="warning"><p>This entry changed, was removed, or needs its current state checked. Your draft is still here. Reload to fetch the current entry; then choose whether to replace your draft.</p><Button variant="secondary" className="mt-2 h-auto min-h-9 w-fit max-w-full whitespace-normal py-2" onClick={() => router.refresh()}>Reload entry</Button><Button variant="secondary" className="mt-2 h-auto min-h-9 w-fit max-w-full whitespace-normal py-2" onClick={useCurrent} disabled={pending || current.entry.state !== "active"}>Replace draft with current entry</Button></Alert>}
      <form className="max-w-160 space-y-5" onSubmit={event => { event.preventDefault(); perform("save"); }}>
        <div className="space-y-1.5"><Label htmlFor="memory-title">Title</Label><Input id="memory-title" value={title} maxLength={120} required disabled={pending} aria-invalid={!!state.fieldErrors?.title} aria-describedby={state.fieldErrors?.title ? "memory-title-error" : undefined} onChange={event => setTitle(event.target.value)} />{state.fieldErrors?.title && <p id="memory-title-error" className="text-small text-destructive-strong">{state.fieldErrors.title.join(" ")}</p>}</div>
        <div className="space-y-1.5"><Label htmlFor="memory-lesson">Lesson</Label><Textarea id="memory-lesson" rows={5} value={lesson} maxLength={500} required disabled={pending} aria-invalid={!!state.fieldErrors?.lesson} aria-describedby="memory-lesson-help memory-lesson-error" onChange={event => setLesson(event.target.value)} /><p id="memory-lesson-help" className="text-small text-muted-foreground">{lesson.length} of 500 characters. Evidence and campaign results stay attached to this entry.</p><p id="memory-lesson-error" className="text-small text-destructive-strong">{state.fieldErrors?.lesson?.join(" ")}</p></div>
        <div className="flex flex-wrap gap-3"><Button type="submit" pending={pending} disabled={stale || !!removal}>Save lesson</Button><Button variant="secondary" className="h-auto min-h-9 max-w-full whitespace-normal py-2" disabled={pending} onClick={useCurrent}>Discard draft changes</Button><Button ref={removeTrigger} className="h-auto min-h-9 max-w-full whitespace-normal py-2" variant="destructive" disabled={pending || stale} onClick={() => { setState({ status: "idle" }); setRemoval({ id: current.entry.id, title: current.entry.title, version: Number(current.entry.version) }); }}>Remove entry</Button></div>
      </form>
    </>}
    <AlertDialog open={removal !== null} onOpenChange={value => { if (!value && !pending) { setRemoval(null); removeTrigger.current?.focus(); } }}><AlertDialogContent initialFocus={cancelRef}><AlertDialogHeader><AlertDialogTitle>Remove {removal?.title}?</AlertDialogTitle><AlertDialogDescription>Future briefs, assets and opportunity rankings will no longer use this entry. Earlier uses and the removed entry remain available for reference. Removal cannot be undone from this screen.</AlertDialogDescription></AlertDialogHeader>{removalStale && <Alert variant="warning">The entry changed. Cancel, reload and review it before removing it.</Alert>}{state.status === "error" && <div ref={removeErrorRef} tabIndex={-1} role="alert" className="text-small text-destructive-strong">{state.message ?? "The entry could not be removed."}</div>}<AlertDialogFooter><AlertDialogCancel ref={cancelRef} disabled={pending}>Cancel</AlertDialogCancel><Button variant="destructive-solid" className="h-auto min-h-9 max-w-full whitespace-normal py-2" pending={pending} disabled={removalStale || !canEdit} onClick={() => perform("remove")}>Remove entry</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </section>;
}
