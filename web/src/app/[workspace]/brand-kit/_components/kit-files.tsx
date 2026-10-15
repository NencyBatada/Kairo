"use client";

import { usePathname, useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";

import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { idle, problemToFormState, type FormState } from "@/lib/forms";
import { REQUEST_HEADER } from "@/lib/http-names";

import { KitConfirm } from "./kit-confirm";
import type { KitAction, KitFile } from "./types";

export function KitFiles({ brandId, files, canEdit, remove }: {
  brandId: string; files: KitFile[]; canEdit: boolean; remove: Record<string, KitAction>;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const selected = useRef<File | null>(null);
  const [state, setState] = useState<FormState>(idle);
  const [pending, setPending] = useState(false);
  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const file = selected.current;
    if (!file || file.size === 0) { setState({ status: "error", message: "Choose a file to upload." }); return; }
    if (file.size > 20 * 1024 * 1024) { setState({ status: "error", message: "Choose a file no larger than 20 MB." }); return; }
    const form = event.currentTarget;
    const data = new FormData(form);
    data.set("file", file);
    setPending(true);
    setState(idle);
    try {
      const response = await fetch(`/api/brands/${brandId}/brand-kit/files`, { method: "POST", headers: { [REQUEST_HEADER]: "1" }, body: data });
      if (response.status === 401) { router.replace(`/sign-in?returnUrl=${encodeURIComponent(pathname)}`); return; }
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        const failure = problemToFormState(problem, response.status);
        setState({ ...failure, message: failure.message ?? Object.values(failure.fieldErrors ?? {}).flat().join(" ") });
        return;
      }
      form.reset(); selected.current = null;
      setState({ status: "saved", message: "File uploaded. Select it in the draft, then save to include it in the kit." });
      router.refresh();
    } catch { setState({ status: "error", message: "Kairo couldn't upload the file. Please try again." }); }
    finally { setPending(false); }
  }
  return <Card><CardHeader><CardTitle>Guideline and logo files</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">
    {canEdit && <form onSubmit={upload} className="flex max-w-160 flex-col gap-4">
      <p className="text-small text-muted-foreground">Upload PDF, PNG, JPEG, WebP, SVG or Word (.docx) files, up to 20 MB. Logos must be images.</p>
      <div className="flex flex-col gap-1.5"><Label htmlFor="kit-file">Choose file</Label><Input id="kit-file" name="file" type="file" accept=".pdf,.png,.jpg,.jpeg,.webp,.svg,.docx" disabled={pending} onChange={(e) => { selected.current = e.target.files?.[0] ?? null; }} /></div>
      <div className="flex flex-col gap-1.5"><Label htmlFor="kit-file-kind">File purpose</Label><NativeSelect id="kit-file-kind" name="kind" disabled={pending}><option value="guideline">Brand guidelines</option><option value="logo">Logo</option></NativeSelect></div>
      <Button type="submit" variant="secondary" pending={pending} className="self-start">Upload file</Button>
      <FormStatus state={state} />
    </form>}
    {!files.length && <p className="text-small text-muted-foreground">No files uploaded yet.</p>}
    <ul className="divide-y divide-border">{files.map((file) => <li key={file.id} className="flex min-w-0 flex-col gap-3 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0"><a href={`/api/brands/${brandId}/brand-kit/files/${file.id}/download`} className="block truncate text-sm font-medium text-primary hover:underline" title={file.fileName}>{file.fileName}</a><p className="text-small text-muted-foreground">{file.kind === "logo" ? "Logo" : "Guideline"} · {(Number(file.size) / 1024).toFixed(1)} KB</p></div>
      {canEdit && <KitConfirm label={`Remove ${file.fileName}`} title="Remove this file?" description="The file will be removed from the draft and available files. Approved and older versions keep their copy." action={remove[file.id]} destructive />}
    </li>)}</ul>
  </CardContent></Card>;
}
