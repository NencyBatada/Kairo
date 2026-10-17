"use client";

import Link from "next/link";
import { AskAboutAsset } from "@/components/ask-kairo/ask-about-asset";
import { Images, LockKeyhole, Sparkles } from "lucide-react";
import type { ReactNode } from "react";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { EmptyState } from "@/components/empty-state";
import { AssetHistory } from "./asset-history";
import { AdPreview, AssetCopy } from "./ad-preview";
import { assetName, campaignTypes, dateTime, languageName, safeUrl, type Asset, type AssetSet } from "../_lib/model";

export function AssetGallery({ set, brandId, campaignId, timeZone, handle, language, campaignType, controls }: { set: AssetSet; brandId: string; campaignId: string; timeZone: string; handle: string; language: string; campaignType: string; controls?: (asset: Asset) => ReactNode }) {
  const all = set.groups.flatMap((group) => group.assets);
  const groups = set.groups.filter((group) => !campaignType || group.campaignType === campaignType);
  const count = groups.reduce((sum, group) => sum + group.assets.length, 0);
  const base = `/${handle}/campaigns/${campaignId}/assets`;
  function href(lang: string, type: string) { const query = new URLSearchParams(); if (lang) query.set("language", lang); if (type) query.set("type", type); return `${base}${query.size ? `?${query}` : ""}`; }
  return <div className="grid min-w-0 gap-8">
    <section aria-labelledby="studio-heading" className="grid min-w-0 gap-4">
      <div><h2 id="studio-heading" className="text-base font-semibold">Creative Studio</h2><p className="mt-1 text-sm text-muted-foreground">Review the saved copy and creatives for each placement. Your ad platform chooses the final combinations.</p></div>
      <nav aria-label="Asset campaign types" className="flex flex-wrap gap-2">{[["", "All types"], ...Object.entries(campaignTypes)].map(([key, label]) => <FilterLink key={key} href={href(language, key)} current={campaignType === key}>{label}</FilterLink>)}</nav>
      {set.languages.length > 0 && <nav aria-label="Asset languages" className="flex flex-wrap gap-2"><FilterLink href={href("", campaignType)} current={!language}>All languages</FilterLink>{set.languages.map((code) => <FilterLink key={code} href={href(code, campaignType)} current={language === code}>{languageName(code)}</FilterLink>)}</nav>}
      <p role="status" className="text-sm text-subtle-foreground">{count} saved {count === 1 ? "asset" : "assets"}{language ? ` · ${languageName(language)} and shared media` : ""}</p>
      {!set.canEdit && all.length > 0 && <Alert variant="info">{set.editBlocker ?? "You have read-only access to these assets. A marketer can make changes while the campaign is creating."}</Alert>}
    </section>
    {groups.length === 0 ? <EmptyState icon={Images} title="No assets yet" description="Assets appear here as Kairo creates them after brief approval. Try another filter if generation has already finished." /> : groups.map((group) => <section key={`${group.campaignType}-${group.language}`} aria-labelledby={`group-${group.campaignType}-${group.language}`} className="grid min-w-0 gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2"><h3 id={`group-${group.campaignType}-${group.language}`} className="text-base font-semibold">{group.label} · {group.languageName ?? languageName(group.language)}</h3><span className="text-xs text-subtle-foreground">{group.assets.length} {group.assets.length === 1 ? "asset" : "assets"}</span></div>
      <div className="grid min-w-0 items-start gap-4 xl:grid-cols-2">{group.assets.map((asset) => <article key={asset.id} id={`asset-${asset.id}`} aria-label={assetName(asset)} className="grid min-w-0 gap-4 rounded-lg border border-border bg-card p-4 md:p-5">
        <div className="min-w-0"><h4 className="font-semibold wrap-anywhere">{assetName(asset)}</h4><div className="mt-2 flex flex-wrap items-center gap-2">
          {asset.locked && <Badge><LockKeyhole aria-hidden="true" className="size-3.5" />Locked</Badge>}
          {!asset.editedByPerson && ["brief_draft", "creating", "asset_review"].includes(set.stage) && <Badge><Sparkles aria-hidden="true" className="size-3.5" />Drafted by Kairo</Badge>}
          <Badge variant={asset.checkState === "passed" ? "success" : asset.checkState === "failed" ? "warning" : "neutral"}>{asset.checkState === "passed" ? "Checks passed" : asset.checkState === "failed" ? "Needs review" : "Not checked yet"}</Badge>
        </div><p className="mt-2 text-xs text-subtle-foreground">Version {asset.version} · {dateTime(asset.updatedAt, timeZone)}</p></div>
        <div lang={asset.language === "zxx" ? undefined : asset.language} className="min-w-0"><AdPreview asset={asset} assets={all} brandId={brandId} campaignId={campaignId} /></div>
        {(asset.content.headlines?.length || asset.content.script?.length) ? <details className="min-w-0"><summary className="cursor-pointer py-2 text-sm font-medium">All copy{asset.content.script?.length ? " and captions" : ""}</summary><div lang={asset.language === "zxx" ? undefined : asset.language} className="mt-3"><AssetCopy asset={asset} /></div></details> : null}
        <AssetDisclosure asset={asset} />
        <AskAboutAsset id={asset.id} campaignId={campaignId} label={assetName(asset)} />
        {controls?.(asset)}
        <AssetHistory asset={asset} brandId={brandId} campaignId={campaignId} timeZone={timeZone} />
      </article>)}</div>
    </section>)}
  </div>;
}

function FilterLink({ href, current, children }: { href: string; current: boolean; children: ReactNode }) {
  return <Link href={href} aria-current={current ? "page" : undefined} className={`inline-flex min-h-9 items-center rounded-md border px-3 py-2 text-sm transition-colors duration-120 pointer-coarse:min-h-11 ${current ? "border-input bg-primary-soft font-medium text-primary-strong" : "border-border bg-card text-muted-foreground hover:bg-accent"}`}>{children}</Link>;
}

function AssetDisclosure({ asset }: { asset: Asset }) {
  const disclosure = asset.disclosure;
  return <div className="grid min-w-0 gap-2 text-xs wrap-anywhere text-muted-foreground">
    <p>{disclosure.aiGenerated ? "AI generated" : "AI generation not recorded"}{disclosure.syntheticMedia ? " · Synthetic media" : ""}{disclosure.watermark ? ` · ${disclosure.watermark}` : ""}</p>
    {disclosure.blocker && <Alert variant="warning">{disclosure.blocker}</Alert>}
    {disclosure.platformLabels.map((label, i) => <div key={i}><p>{label.platform} · {label.setting}</p><p>{label.reason}</p>{safeUrl(label.source) && <a className="text-primary underline underline-offset-4" href={safeUrl(label.source)!} target="_blank" rel="noopener noreferrer">Disclosure requirements<span className="sr-only"> (opens in a new tab)</span></a>}</div>)}
  </div>;
}
