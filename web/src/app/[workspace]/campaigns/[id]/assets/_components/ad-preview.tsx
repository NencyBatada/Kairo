"use client";

import { useId, useState } from "react";
import { NativeSelect } from "@/components/ui/native-select";
import { AssetMedia } from "./asset-media";
import { matchingImage, safeUrl, type Asset } from "../_lib/model";

export function AdPreview({ asset, assets, brandId, campaignId }: { asset: Asset; assets: Asset[]; brandId: string; campaignId: string }) {
  const { content } = asset;
  const id = useId();
  const [selection, setSelection] = useState(0);
  const index = selection < (content.headlines?.length ?? 0) ? selection : 0;
  const url = safeUrl(content.finalUrl);
  const image = matchingImage(asset, assets);
  if (["image", "video", "voiceover", "footage"].includes(asset.kind)) return <AssetMedia asset={asset} brandId={brandId} campaignId={campaignId} />;
  if (asset.kind === "keyword_set") return <ul className="divide-y divide-border rounded-md border border-border bg-lightbox px-4">{content.keywords?.map((keyword, i) => <li key={i} className="flex flex-wrap justify-between gap-2 py-3 text-sm"><span className="wrap-anywhere">{keyword.match === "exact" ? `[${keyword.text}]` : keyword.match === "phrase" ? `“${keyword.text}”` : keyword.text}</span><span className="text-xs text-subtle-foreground">{keyword.match} match</span></li>)}</ul>;
  if (asset.kind === "shopping_title") return <div className="grid min-w-0 gap-3 rounded-md border border-border bg-lightbox p-4">{image ? <AssetMedia asset={image} brandId={brandId} campaignId={campaignId} /> : <p className="text-xs text-subtle-foreground">No product image supplied for this preview.</p>}<p className="wrap-anywhere text-base">{content.title}</p><p className="text-xs text-subtle-foreground">Shopping preview · Price and store details appear at launch.</p></div>;
  return <div className="grid min-w-0 gap-3">
    <div className="rounded-md border border-border bg-lightbox p-4">
      <p className="mb-2 text-xs font-semibold">Sponsored{content.businessName ? ` · ${content.businessName}` : ""}</p>
      {asset.kind === "demand_gen_text" && image && <div className="mb-4"><AssetMedia asset={image} brandId={brandId} campaignId={campaignId} /></div>}
      {url && <p className="mb-2 text-xs wrap-anywhere text-muted-foreground">{new URL(url).hostname}{[content.path1, content.path2].filter(Boolean).map((part) => ` › ${part}`).join("")}</p>}
      <p className="text-lg leading-snug font-medium wrap-anywhere text-info-strong">{asset.kind === "search_ad" ? [0, 1, 2].map((offset) => content.headlines?.[(index + offset) % (content.headlines?.length || 1)]).filter(Boolean).join(" | ") : content.headlines?.[index]}</p>
      <p className="mt-2 text-sm wrap-anywhere">{asset.kind === "search_ad" ? content.descriptions?.slice(0, 2).join(" ") : content.descriptions?.[0]}</p>
      {content.callToAction && <p className="mt-4 w-fit rounded-md border border-input px-3 py-2 text-sm font-medium">{content.callToAction.replaceAll("_", " ")}</p>}
    </div>
    {(content.headlines?.length ?? 0) > 1 && <div className="grid gap-1.5"><label className="text-xs text-subtle-foreground" htmlFor={id}>Preview headline</label><NativeSelect id={id} value={index} onChange={(event) => setSelection(Number(event.target.value))}>{content.headlines?.map((text, i) => <option key={i} value={i}>{text}</option>)}</NativeSelect></div>}
    <p className="text-xs text-subtle-foreground">Illustrative {asset.kind === "search_ad" ? "Search" : "Demand Gen"} preview. Placements and combinations may vary.</p>
  </div>;
}

export function AssetCopy({ asset }: { asset: Pick<Asset, "content"> }) {
  const content = asset.content;
  return <div className="grid gap-4 text-sm">
    {(["headlines", "longHeadlines", "descriptions"] as const).map((field) => content[field]?.length ? <div key={field}><p className="mb-1 font-medium">{{ headlines: "Headlines", longHeadlines: "Long headlines", descriptions: "Descriptions" }[field]}</p><ul className="list-inside list-disc space-y-1 wrap-anywhere">{content[field]?.map((text, i) => <li key={i}>{text}</li>)}</ul></div> : null)}
    {content.script?.length ? <div><p className="mb-2 font-medium">Script and captions</p><ol className="space-y-3">{content.script.map((line, i) => <li key={i} className="wrap-anywhere"><p className="text-xs text-subtle-foreground">{line.start} to {line.end} seconds</p><p>{line.voiceover}</p><p className="text-muted-foreground">Caption: {line.caption}</p></li>)}</ol></div> : null}
  </div>;
}
