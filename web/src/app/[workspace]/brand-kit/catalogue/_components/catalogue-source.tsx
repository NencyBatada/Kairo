"use client";

import { usePathname, useRouter } from "next/navigation";
import { useActionState, useRef, useState, type FormEvent } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle, AlertDialogTrigger } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { idle, problemToFormState } from "@/lib/forms";
import { REQUEST_HEADER } from "@/lib/http-names";

import { importOutcome } from "../_lib/catalogue";
import type { CatalogueAction, CatalogueActionState, CatalogueImport, CatalogueSource as Source } from "../_lib/types";
import { ImportResult } from "./import-result";

export function CatalogueSource({ brandId, handle, source, canManage, setFeed, refreshFeed, stopFeed }: {
  brandId: string; handle: string; source: Source | null; canManage: boolean;
  setFeed: CatalogueAction; refreshFeed: CatalogueAction; stopFeed: CatalogueAction;
}) {
  const router = useRouter(); const pathname = usePathname();
  const selected = useRef<File | null>(null);
  const [uploadState, setUploadState] = useState<CatalogueActionState>(idle);
  const [uploading, setUploading] = useState(false);
  const [feedState, feedAction, saving] = useActionState<CatalogueActionState, FormData>(setFeed, idle);
  const [refreshState, refreshAction, refreshing] = useActionState<CatalogueActionState, FormData>(refreshFeed, idle);
  const [stopState, stopAction, stopping] = useActionState<CatalogueActionState, FormData>(stopFeed, idle);
  const [feedUrl, setFeedUrl] = useState(source?.feedUrl ?? "");
  const [stopOpen, setStopOpen] = useState(false);
  const [reviewedFeed, setReviewedFeed] = useState(source?.feedUrl);
  const busy = uploading || saving || refreshing || stopping;
  const errors = feedState.fieldErrors ?? {};

  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const file = selected.current;
    if (!file || file.size === 0) { setUploadState({ status: "error", message: "Choose a catalogue file to import." }); return; }
    if (file.size > 50 * 1024 * 1024) { setUploadState({ status: "error", message: "Choose a file no larger than 50 MB." }); return; }
    const data = new FormData(event.currentTarget);
    data.set("file", file);
    data.set("allowLargeRemoval", data.get("allowLargeRemoval") === "on" ? "true" : "false");
    setUploading(true); setUploadState(idle);
    try {
      const response = await fetch(`/api/brands/${brandId}/catalogue/imports`, { method: "POST", headers: { [REQUEST_HEADER]: "1" }, body: data });
      if (response.status === 401) { router.replace(`/sign-in?returnUrl=${encodeURIComponent(pathname)}`); return; }
      const result = await response.json().catch(() => null);
      if (!response.ok) { const error = problemToFormState(result, response.status); setUploadState({ ...error, message: error.message ?? Object.values(error.fieldErrors ?? {}).flat().join(" ") }); return; }
      if (!result || !["succeeded", "unchanged", "failed", "suspicious"].includes(result.status)) { setUploadState({ status: "error", message: "Kairo couldn't read the import result. Reload the catalogue before trying again." }); return; }
      setUploadState({ ...importOutcome(result), import: result as CatalogueImport }); router.refresh();
    } catch { setUploadState({ status: "error", message: "Kairo couldn't import the file. Please try again." }); }
    finally { setUploading(false); }
  }

  return <Card><CardHeader><CardTitle>Catalogue source</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">
    <p className="text-small wrap-anywhere text-muted-foreground">{source?.kind === "feed" ? `Feed: ${source.feedUrl}` : source?.kind === "file" ? "Products come from a file. Upload a new snapshot to update them." : "No catalogue source yet. Import a file or connect a feed."}</p>
    {source?.kind === "feed" && <p className="text-small text-muted-foreground">The feed refreshes every hour. Failed checks in a row: {source.failuresInRow}.</p>}
    {canManage && <>
      <form onSubmit={upload} className="flex max-w-160 flex-col gap-4">
        <h3 className="text-sm font-semibold">Import a file</h3>
        <p className="text-small text-muted-foreground">CSV, TSV, RSS or Atom XML in Merchant Center format, up to 50 MB unpacked and 50,000 products. Each import replaces the full catalogue and stops feed refreshes.</p>
        <div className="flex flex-col gap-1.5"><Label htmlFor="catalogue-file">Catalogue file</Label><Input id="catalogue-file" name="file" type="file" accept=".csv,.tsv,.xml,.gz" disabled={busy} onChange={(e) => { selected.current = e.target.files?.[0] ?? null; }} /></div>
        {source?.kind === "feed" && <label className="flex min-h-6 items-start gap-2 text-small pointer-coarse:min-h-11"><Checkbox name="replaceFeed" required disabled={busy} /><span>I understand that importing a file stops the connected feed.</span></label>}
        <label className="flex min-h-6 items-start gap-2 text-small pointer-coarse:min-h-11"><Checkbox name="allowLargeRemoval" disabled={busy} /><span>Allow this import to remove more than half the catalogue. Only select this after reviewing a held import.</span></label>
        <Button type="submit" variant="secondary" pending={uploading} disabled={busy} className="self-start">Import file</Button>
        {uploadState.import ? <ImportResult result={uploadState.import} handle={handle} /> : <FormStatus state={uploadState} />}
      </form>
      <form action={feedAction} className="flex max-w-160 flex-col gap-4 border-t border-border pt-5">
        <h3 className="text-sm font-semibold">Connect a feed</h3>
        <p className="text-small text-muted-foreground">Saving a feed imports its full catalogue now and refreshes it hourly.</p>
        <div className="flex flex-col gap-1.5"><Label htmlFor="feed-url">Feed URL</Label><Input id="feed-url" name="url" type="url" required value={feedUrl} onChange={(e) => setFeedUrl(e.target.value)} disabled={busy} aria-invalid={errors.url?.length ? true : undefined} aria-describedby="feed-url-error" /><FieldError id="feed-url-error" errors={errors.url} /></div>
        <Button type="submit" variant="secondary" pending={saving} disabled={busy} className="self-start">Save feed</Button>
        {feedState.import ? <ImportResult result={feedState.import} handle={handle} /> : <FormStatus state={feedState} />}
      </form>
      {source?.kind === "feed" && <div className="flex flex-col gap-4 border-t border-border pt-5">
        <form action={refreshAction} className="flex flex-col gap-3"><Button type="submit" variant="secondary" pending={refreshing} disabled={busy} className="self-start">Refresh feed now</Button>{refreshState.import ? <ImportResult result={refreshState.import} handle={handle} /> : <FormStatus state={refreshState} />}</form>
        <AlertDialog open={stopOpen} onOpenChange={(open) => { if (!stopping) { if (open) setReviewedFeed(source.feedUrl); setStopOpen(open); } }}>
          <AlertDialogTrigger render={<Button variant="destructive" disabled={busy} className="self-start" />}>Stop refreshing feed</AlertDialogTrigger>
          <AlertDialogContent><AlertDialogTitle>Stop refreshing this feed?</AlertDialogTitle><AlertDialogDescription>Products stay in the catalogue. Stock and other details will no longer update automatically.</AlertDialogDescription>
            <form action={stopAction} className="flex flex-col gap-4">{reviewedFeed !== source.feedUrl && <Alert variant="warning">The feed changed. Cancel and review the latest source.</Alert>}<FormStatus state={stopState} /><AlertDialogFooter><AlertDialogCancel disabled={stopping}>Cancel</AlertDialogCancel><Button type="submit" variant="destructive-solid" pending={stopping} disabled={reviewedFeed !== source.feedUrl}>Stop refreshing feed</Button></AlertDialogFooter></form>
          </AlertDialogContent>
        </AlertDialog>
      </div>}
    </>}
  </CardContent></Card>;
}
