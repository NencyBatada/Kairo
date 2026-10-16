"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Alert } from "@/components/ui/alert";
import type { components } from "@/lib/core-api/schema";
import type { Asset, AssetSet } from "../_lib/model";
import { AssetGallery } from "./asset-gallery";
import { AssetControls } from "./asset-controls";

type Run = components["schemas"]["CampaignRunView"];
export function StudioWorkspace(props: { set: AssetSet; brandId: string; campaignId: string; timeZone: string; handle: string; language: string; campaignType: string; runs: Run[] }) {
  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeSource, setActiveSource] = useState<{ asset: Asset; group: AssetSet["groups"][number] } | null>(null);
  const [queuedId, setQueuedId] = useState<string | null>(null);
  const [notice, setNotice] = useState(false);
  const noticeRef = useRef<HTMLDivElement>(null);
  const [saved, setSaved] = useState<Record<string, { source: string; asset: Asset }>>({});
  const queued = queuedId ? props.runs.find((run) => run.id === queuedId) : null;
  const waiting = !!queuedId && (!queued || ["queued", "running", "retrying"].includes(queued.status));
  const latest = [...props.runs].filter((run) => run.kind === "asset_regenerate").sort((a, b) => b.queuedAt.localeCompare(a.queuedAt))[0];
  useEffect(() => {
    if (!activeId) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    const follow = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>("a[href]") : null;
      if (!link || link.target === "_blank" || link.hasAttribute("download")) return;
      const url = new URL(link.href, window.location.href);
      if (url.origin === location.origin && url.pathname === location.pathname && url.search === location.search && url.hash) return;
      event.preventDefault(); event.stopPropagation(); setNotice(true);
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", follow, true);
    return () => { window.removeEventListener("beforeunload", warn); document.removeEventListener("click", follow, true); };
  }, [activeId]);
  useEffect(() => { if (notice) noticeRef.current?.focus(); }, [notice]);
  const set = { ...props.set, groups: props.set.groups.map((group) => ({ ...group, assets: group.assets.map((asset) => saved[asset.id]?.source === JSON.stringify(asset) ? saved[asset.id].asset : asset) })) };
  const removed = !!activeId && !props.set.groups.some((group) => group.assets.some((asset) => asset.id === activeId));
  if (removed && activeSource) {
    const existing = set.groups.find((group) => group.campaignType === activeSource.group.campaignType && group.language === activeSource.group.language);
    if (existing) existing.assets.push(activeSource.asset);
    else set.groups.push({ ...activeSource.group, assets: [activeSource.asset] });
  }
  return <div className="grid min-w-0 gap-5">
    {activeId && <p role="status" className="text-sm text-muted-foreground">Finish or discard this asset’s changes before editing another asset or changing filters.</p>}
    {notice && activeId && <div ref={noticeRef} tabIndex={-1} className="outline-none"><Alert variant="warning">Your unsaved work is still here. Save or discard it before following this link.</Alert></div>}
    {removed && <Alert variant="warning">This asset is no longer in the current set. Your unsaved draft is kept here so you can copy it before discarding.</Alert>}
    {waiting && <Alert variant="info">Regeneration is queued or running. Saved assets remain visible while Kairo works.</Alert>}
    {latest?.status === "failed" && <Alert variant="warning"><p>{latest.error ?? "Asset regeneration failed. The previous saved version is still available."}</p><Link href={`/${props.handle}/campaigns/${props.campaignId}`} className="mt-2 inline-block text-primary underline underline-offset-4">View progress and retry</Link></Alert>}
    <AssetGallery {...props} set={set} controls={(asset) => <AssetControls asset={asset} brandId={props.brandId} campaignId={props.campaignId} allowed={props.set.canEdit && !(removed && activeId === asset.id)} disabled={waiting || (!!activeId && activeId !== asset.id)} onActive={(active) => { setActiveId(active ? asset.id : null); const group = props.set.groups.find((entry) => entry.assets.some((candidate) => candidate.id === asset.id)); setActiveSource(active && group ? { asset, group } : null); setNotice(false); }} onSaved={(next) => { const original = props.set.groups.flatMap((group) => group.assets).find((entry) => entry.id === next.id); if (original) setSaved((current) => ({ ...current, [next.id]: { source: JSON.stringify(original), asset: next } })); }} onQueued={setQueuedId} />} />
  </div>;
}
