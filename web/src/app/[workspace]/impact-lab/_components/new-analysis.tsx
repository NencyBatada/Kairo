"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, useTransition } from "react";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { createAnalysis } from "../actions";
import { setupErrors, type LabState, type Setup } from "../_lib/model";
import { SetupForm } from "./setup-form";

export function NewAnalysis({ brandId, workspace, canEdit }: { brandId: string; workspace: string; canEdit: boolean }) {
  const router = useRouter(), [state, setState] = useState<LabState>({ status: "idle" }), [pending, start] = useTransition(), busy = useRef(false), notice = useRef<HTMLDivElement>(null);
  useEffect(() => { if (state.status !== "idle") notice.current?.focus(); }, [state]);
  function save(setup: Setup) {
    if (busy.current || state.conflict || state.signIn || !canEdit) return;
    const errors = setupErrors(setup);
    if (Object.keys(errors).length) { setState({ status: "error", fieldErrors: errors }); return; }
    busy.current = true;
    start(async () => {
      try { const result = await createAnalysis(brandId, setup); setState(result); if (result.analysis) router.push(`/${workspace}/impact-lab/${result.analysis.id}`); }
      catch { setState({ status: "error", conflict: true, message: "Creation could not be confirmed. Check the analysis history before creating another. Your draft is still here." }); }
      finally { busy.current = false; }
    });
  }
  if (!canEdit) return <Alert variant="neutral">Only marketers and admins can create or run analyses. You can read existing analyses from Impact Lab.</Alert>;
  return <section className="space-y-5 rounded-lg border border-border bg-card p-4 md:p-6">{state.status !== "idle" && <div ref={notice} role={state.status === "error" ? "alert" : "status"} tabIndex={-1} className="space-y-2 text-small"><p>{state.message ?? "Check the fields below."}</p>{state.signIn && <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/sign-in?returnUrl=${encodeURIComponent(`/${workspace}/impact-lab/new`)}`} target="_blank" rel="noopener">Sign in again in a new tab</Link>}{state.signIn && <Button variant="secondary" className="h-auto min-h-11 w-fit whitespace-normal py-2" onClick={() => setState({ status: "idle" })}>Continue after signing in</Button>}{state.conflict && <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/${workspace}/impact-lab`} target="_blank" rel="noopener">Check analysis history in a new tab</Link>}</div>}<SetupForm pending={pending} state={state} blocked={state.conflict || state.signIn || state.status === "saved"} onSave={save} /></section>;
}
