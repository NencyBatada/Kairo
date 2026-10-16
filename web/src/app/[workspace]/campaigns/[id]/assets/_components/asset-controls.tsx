"use client";

import { useEffect, useId, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { regenerateAsset, saveAsset, setAssetLock } from "../actions";
import { regenerationKinds, textKinds, type AssetState } from "../_lib/edit";
import { assetName, type Asset } from "../_lib/model";
import { AssetFields } from "./asset-fields";

export function AssetControls({ asset, brandId, campaignId, allowed, disabled, onActive, onSaved, onQueued }: { asset: Asset; brandId: string; campaignId: string; allowed: boolean; disabled: boolean; onActive: (active: boolean) => void; onSaved: (asset: Asset) => void; onQueued: (runId: string) => void }) {
  const router = useRouter(); const id = useId();
  const [mode, setMode] = useState<"edit" | "regenerate" | null>(null);
  const [baseline, setBaseline] = useState(asset);
  const [state, setState] = useState<AssetState>({ status: "idle" });
  const [instruction, setInstruction] = useState("");
  const [confirmation, setConfirmation] = useState<"discard" | "regenerate" | null>(null);
  const [busy, start] = useTransition();
  const errorRef = useRef<HTMLDivElement>(null); const dialogErrorRef = useRef<HTMLDivElement>(null); const cancelRef = useRef<HTMLButtonElement>(null); const editRef = useRef<HTMLButtonElement>(null); const regenRef = useRef<HTMLButtonElement>(null);
  const stale = mode !== null && (String(asset.version) !== String(baseline.version) || asset.locked !== baseline.locked || state.conflict);
  const blocked = !allowed || disabled || busy || !!stale;
  useEffect(() => { if (state.status === "error") (confirmation ? dialogErrorRef : errorRef).current?.focus(); }, [state, confirmation]);
  function open(next: "edit" | "regenerate") { setBaseline(asset); setState({ status: "idle" }); setInstruction(""); setMode(next); onActive(true); }
  function close() { const current = mode; setMode(null); setConfirmation(null); onActive(false); requestAnimationFrame(() => (current === "edit" ? editRef : regenRef).current?.focus()); }
  function accept(result: AssetState) { setState(result); if (result.asset) onSaved(result.asset); }
  return <div className="grid min-w-0 gap-4">
    {asset.kind === "shopping_title" && <div className="grid min-w-0 gap-3 rounded-md bg-muted p-3 text-sm"><div><p className="text-xs text-subtle-foreground">Current catalogue title</p><p className="mt-1 wrap-anywhere" lang={asset.language}>{asset.content.currentTitle ?? "Current title was not recorded."}</p></div><div><p className="text-xs text-subtle-foreground">Campaign title</p><p className="mt-1 wrap-anywhere" lang={asset.language}>{asset.content.title}</p></div><p className="text-xs text-subtle-foreground">Saving changes the campaign asset. The catalogue stays unchanged.</p></div>}
    {(allowed || mode) && <>
      {!mode ? <div className="flex flex-wrap gap-2">
        {textKinds.includes(asset.kind) && <Button ref={editRef} variant="secondary" disabled={disabled || asset.locked || busy} onClick={() => open("edit")}>Edit asset</Button>}
        {regenerationKinds.includes(asset.kind) && <Button ref={regenRef} variant="secondary" disabled={disabled || asset.locked || busy} onClick={() => open("regenerate")}>Regenerate</Button>}
        <Button variant="ghost" disabled={disabled || busy} pending={busy} onClick={() => { onActive(true); start(async () => { try { accept(await setAssetLock(brandId, campaignId, asset.id, asset.version, !asset.locked)); } finally { onActive(false); } }); }}>{asset.locked ? "Unlock asset" : "Lock asset"}</Button>
      </div> : <>
        {stale && <Alert variant="warning">This asset changed since you opened it. Your edits are still here. Discard them and reload the latest version before continuing.</Alert>}
        {!allowed && <Alert variant="info">Editing is paused. Your changes are kept here until you can save or discard them.</Alert>}
        {mode === "edit" ? <form noValidate onSubmit={(event) => { event.preventDefault(); const form = new FormData(event.currentTarget); if (blocked) return; start(async () => { const result = await saveAsset(brandId, campaignId, baseline.id, baseline.kind, form); accept(result); if (result.status === "saved") close(); }); }} className="grid min-w-0 gap-5">
          <input type="hidden" name="version" value={String(baseline.version)} />
          <fieldset disabled={busy} className="min-w-0"><legend className="mb-4 text-sm font-semibold">Edit {assetName(baseline)}</legend><AssetFields asset={baseline} errors={state.fieldErrors} /></fieldset>
          <div className="flex flex-wrap gap-2"><Button type="submit" disabled={blocked} pending={busy}>Save asset</Button><Button variant="secondary" disabled={busy} onClick={() => setConfirmation("discard")}>{stale ? "Reload latest" : "Discard edits"}</Button></div>
        </form> : <div className="grid gap-3"><label htmlFor={id} className="text-sm font-medium">Regeneration instruction</label><p id={`${id}-help`} className="text-xs text-subtle-foreground">Describe the change in 2 to 500 characters. Kairo will save a new version automatically; earlier versions stay in history.</p><Textarea id={id} value={instruction} onChange={(event) => setInstruction(event.target.value)} disabled={busy} aria-invalid={!!state.fieldErrors?.instruction} aria-describedby={`${id}-help${state.fieldErrors?.instruction ? ` ${id}-error` : ""}`} /><FieldError id={`${id}-error`} errors={state.fieldErrors?.instruction} /><div className="flex flex-wrap gap-2"><Button variant="secondary" disabled={blocked} onClick={() => { if (instruction.trim().length < 2 || instruction.trim().length > 500) setState({ status: "error", message: "Describe the change in 2 to 500 characters.", fieldErrors: { instruction: ["Describe the change in 2 to 500 characters."] } }); else { setState({ status: "idle" }); setConfirmation("regenerate"); } }}>Review regeneration</Button><Button variant="ghost" disabled={busy} onClick={() => setConfirmation("discard")}>{stale ? "Reload latest" : "Discard instruction"}</Button></div></div>}
      </>}
      {state.message && !confirmation && <div ref={errorRef} tabIndex={state.status === "error" ? -1 : undefined} className="outline-none"><FormStatus state={state} /></div>}
      {state.conflict && !mode && <Button variant="secondary" className="w-fit" onClick={() => { setState({ status: "idle" }); router.refresh(); }}>Reload latest</Button>}
    </>}
    {asset.locked && <p className="text-xs text-muted-foreground">Locked assets are kept when Kairo regenerates the campaign. Unlock this asset to change it.</p>}
    <AlertDialog open={confirmation !== null} onOpenChange={(open) => { if (!open && !busy) setConfirmation(null); }}>
      <AlertDialogContent initialFocus={cancelRef}>
        <AlertDialogTitle>{confirmation === "discard" ? "Discard unsaved changes?" : `Regenerate ${assetName(baseline)}?`}</AlertDialogTitle>
        <AlertDialogDescription>{confirmation === "discard" ? "Your unsaved edits or instruction will be lost. The saved asset stays unchanged." : "Kairo will replace this asset with a new saved version when generation finishes. Earlier versions stay in history. Other assets may include newly generated supporting media for a video."}</AlertDialogDescription>
        {confirmation === "regenerate" && <><p className="text-sm wrap-anywhere">{instruction}</p><div ref={dialogErrorRef} tabIndex={-1} className="outline-none"><FormStatus state={state} /></div>{stale && <Alert variant="warning">The asset changed. Cancel and reload its latest version.</Alert>}</>}
        <AlertDialogFooter><AlertDialogCancel ref={cancelRef} disabled={busy}>Cancel</AlertDialogCancel><Button variant={confirmation === "discard" ? "destructive-solid" : "default"} pending={busy} disabled={confirmation === "regenerate" && blocked} onClick={() => {
          if (confirmation === "discard") { close(); setState({ status: "idle" }); if (stale) router.refresh(); }
          else start(async () => { const result = await regenerateAsset(brandId, campaignId, baseline.id, baseline.version, instruction); accept(result); if (result.status === "saved") { if (result.runId) onQueued(result.runId); close(); } });
        }}>{confirmation === "discard" ? "Discard changes" : "Regenerate asset"}</Button></AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>;
}
