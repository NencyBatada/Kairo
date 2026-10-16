"use client";

import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/alert";
import { Skeleton } from "@/components/ui/skeleton";
import { loadAssetHistory } from "../history-action";
import { assetName, dateTime, safeUrl, type Asset, type AssetHistory as History } from "../_lib/model";
import { AssetCopy } from "./ad-preview";
import { AssetMedia } from "./asset-media";

export function AssetHistory({ asset, brandId, campaignId, timeZone }: { asset: Asset; brandId: string; campaignId: string; timeZone: string }) {
  const id = useId();
  const [open, setOpen] = useState(false);
  const [state, setState] = useState<{ history?: History; error?: string }>({});
  const [busy, setBusy] = useState(false);
  async function load() {
    setBusy(true);
    try { setState(await loadAssetHistory(brandId, campaignId, asset.id)); }
    catch { setState({ error: "Asset history could not be loaded. Try again." }); }
    finally { setBusy(false); }
  }
  return <div className="border-t border-border pt-4">
    <Button variant="ghost" aria-expanded={open} aria-controls={id} onClick={() => { setOpen(!open); if (!open) void load(); }}>{open ? "Hide history" : "View history"}<span className="sr-only"> for {assetName(asset)}</span></Button>
    {open && <div id={id} aria-busy={busy} className="mt-3 min-w-0 space-y-4">
      {busy ? <><p role="status" className="text-sm text-muted-foreground">Loading history…</p><Skeleton className="h-20" /></> : state.error ? <Alert variant="destructive"><p>{state.error}</p><Button variant="secondary" onClick={load} className="mt-3">Try history again</Button></Alert> : <>
        {state.history?.versions.length === 0 && <p className="text-sm text-muted-foreground">No version history is available.</p>}
        {state.history?.versions.map((entry) => <details key={String(entry.version)} className="min-w-0 rounded-md border border-border p-3">
          <summary className="cursor-pointer py-1 text-sm font-medium wrap-anywhere">Version {entry.version} · {entry.origin} · {dateTime(entry.createdAt, timeZone)}</summary>
          <div className="mt-4 grid min-w-0 gap-4 text-sm wrap-anywhere">
            <p>{entry.createdBy?.name ?? "Kairo"}{entry.approvedBy ? ` · Approved by ${entry.approvedBy.name}${entry.approvedAt ? ` on ${dateTime(entry.approvedAt, timeZone)}` : ""}` : " · Not approved"}</p>
            {entry.instruction && <p><strong>Instruction:</strong> {entry.instruction}</p>}
            {entry.changedFields.length > 0 && <p>Changed: {entry.changedFields.join(", ")}</p>}
            <div lang={asset.language === "zxx" ? undefined : asset.language} className="grid min-w-0 gap-3">
              {entry.content.title && <p>{entry.content.title}</p>}
              {entry.content.keywords && <ul>{entry.content.keywords.map((keyword, i) => <li key={i}>{keyword.text} · {keyword.match}</li>)}</ul>}
              <AssetCopy asset={{ content: entry.content }} />
            </div>
            {entry.mediaUrl && <AssetMedia asset={{ ...asset, version: entry.version, mediaUrl: entry.mediaUrl }} brandId={brandId} campaignId={campaignId} />}
            <dl className="grid grid-cols-1 gap-2 text-xs text-muted-foreground"><div><dt className="font-medium">Model and prompt</dt><dd>{entry.model ?? "Not recorded"} · {entry.promptVersion ?? "Not recorded"}</dd></div><div><dt className="font-medium">Source versions</dt><dd>Brief {entry.briefVersion} · Brand Kit {entry.brandKitVersion ?? "not recorded"}</dd></div></dl>
            {entry.sources.photoUrls.length > 0 && <ul className="space-y-2">{entry.sources.photoUrls.map((source, i) => safeUrl(source) ? <li key={i}><a className="text-primary underline underline-offset-4" href={safeUrl(source)!} target="_blank" rel="noopener noreferrer">Source photo {i + 1}<span className="sr-only"> (opens in a new tab)</span></a></li> : <li key={i}>Source photo unavailable</li>)}</ul>}
            {entry.sources.productIds.length > 0 && <p className="text-xs text-muted-foreground">Source products: {entry.sources.productIds.join(", ")}</p>}
            {entry.sources.inputs.length > 0 && <ul className="text-xs text-muted-foreground">{entry.sources.inputs.map((source, i) => <li key={i}>{source.kind} · version {source.version} · <span className="font-mono">{source.assetId}</span></li>)}</ul>}
            {entry.memoryUses.length > 0 && <p>Brand Memory used: {entry.memoryUses.join(", ")}</p>}
            <p className="text-xs text-muted-foreground">{entry.disclosure.aiGenerated ? "AI generated" : "AI generation not recorded"}{entry.disclosure.syntheticMedia ? " · Synthetic media" : ""}{entry.disclosure.watermark ? ` · ${entry.disclosure.watermark}` : ""}</p>
            {entry.disclosure.blocker && <Alert variant="warning">{entry.disclosure.blocker}</Alert>}
            {entry.disclosure.platformLabels.map((label, i) => <p key={i} className="text-xs text-muted-foreground">{label.platform}: {label.setting} · {label.reason}</p>)}
          </div>
        </details>)}
      </>}
    </div>}
  </div>;
}
