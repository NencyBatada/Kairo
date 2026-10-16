"use client";

import Image from "next/image";
import { useState } from "react";
import type { Asset } from "../_lib/model";
import { assetName, mediaUrl } from "../_lib/model";

export function AssetMedia({ asset, brandId, campaignId }: { asset: Asset; brandId: string; campaignId: string }) {
  const url = mediaUrl(asset.mediaUrl, brandId, campaignId, asset.id, asset.version);
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const width = Number(asset.width), height = Number(asset.height);
  const ratio = width > 0 && height > 0 && Number.isFinite(width / height) ? `${width} / ${height}` : asset.kind === "video" || asset.kind === "footage" ? "9 / 16" : "1 / 1";
  return <div className="min-w-0 rounded-md border border-border bg-lightbox p-3">
    {!url || failedUrl === url ? <p className="py-8 text-center text-sm text-muted-foreground">Media unavailable. Try reloading the campaign.</p> : asset.kind === "image" ?
      <div className="relative mx-auto max-h-96 w-full" style={{ aspectRatio: ratio }}><Image src={url} alt={asset.productIds.length ? `Generated product creative · ${asset.format ?? "image"}` : assetName(asset)} fill unoptimized sizes="(max-width: 767px) 100vw, 50vw" className="object-contain" onError={() => setFailedUrl(url)} /></div> : asset.kind === "voiceover" ?
      <audio aria-label={assetName(asset)} controls preload="none" src={url} className="w-full" onError={() => setFailedUrl(url)}>Your browser cannot play this audio.</audio> :
      <video aria-label={assetName(asset)} controls playsInline preload="none" src={url} className="mx-auto max-h-120 w-full object-contain" style={{ aspectRatio: ratio }} onError={() => setFailedUrl(url)}>Your browser cannot play this video.</video>}
    {asset.durationSeconds !== null && <p className="mt-2 text-xs text-subtle-foreground">{asset.durationSeconds} seconds{asset.width && asset.height ? ` · ${asset.width} × ${asset.height}` : ""}</p>}
  </div>;
}
