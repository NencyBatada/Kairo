"use client";

import { useActionState, useState } from "react";

import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { idle } from "@/lib/forms";

import type { KitAction, KitRun } from "./types";

export function DraftingForm({ run, hasDraft, canEdit, action }: { run: KitRun | null; hasDraft: boolean; canEdit: boolean; action: KitAction }) {
  const [state, submit, pending] = useActionState(action, idle);
  const [website, setWebsite] = useState(run?.websiteUrl ?? "");
  const [replace, setReplace] = useState(false);
  const running = run?.status === "pending" || run?.status === "running";
  return <Card><CardHeader><CardTitle>Start with your website</CardTitle></CardHeader><CardContent className="flex flex-col gap-4">
    <p className="max-w-prose text-small text-muted-foreground">Kairo drafts your voice, colours, logo, messages and categories from your website and best-performing ads. You review and approve what agents use.</p>
    {running && <Alert variant="info" role="status">Reading your website and ads. This page updates when your draft is ready.</Alert>}
    {run?.status === "failed" && <Alert variant="warning">{run.error?.message ?? "Kairo couldn't draft the kit. Try again, or fill it in by hand below."}</Alert>}
    {run?.status === "succeeded" && <Alert variant="success">Kairo drafted version {run.version}. Review the draft below before approving it.</Alert>}
    {canEdit && <form action={submit} className="flex max-w-160 flex-col gap-4">
      <div className="flex flex-col gap-1.5"><Label htmlFor="brand-website">Brand website</Label><Input id="brand-website" name="websiteUrl" type="url" required maxLength={2000} value={website} onChange={(e) => setWebsite(e.target.value)} disabled={pending || running} placeholder="https://your-brand.example" /></div>
      {hasDraft && <label className="flex min-h-6 items-start gap-2 text-small pointer-coarse:min-h-11"><Checkbox name="replaceDraft" checked={replace} disabled={pending || running} onChange={(e) => setReplace(e.target.checked)} /><span>Replace the current draft with a new one. The approved kit stays in use.</span></label>}
      <Button type="submit" variant="secondary" pending={pending} disabled={running || (hasDraft && !replace)} className="self-start">Draft from website</Button>
      <FormStatus state={state} />
      {state.fieldErrors && <Alert variant="destructive">{Object.values(state.fieldErrors).flat().join(" ")}</Alert>}
    </form>}
  </CardContent></Card>;
}
