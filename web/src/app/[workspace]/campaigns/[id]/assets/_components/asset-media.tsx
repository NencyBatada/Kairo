"use client";

import { AccessibleMedia } from "@/components/accessible-media";
import type { Asset } from "../_lib/model";
import { assetName, mediaUrl } from "../_lib/model";

export function AssetMedia({ asset, brandId, campaignId }: { asset: Asset; brandId: string; campaignId: string }) {
  const url = mediaUrl(asset.mediaUrl, brandId, campaignId, asset.id, asset.version);
  const width = Number(asset.width), height = Number(asset.height);
  const ratio = width > 0 && height > 0 && Number.isFinite(width / height) ? `${width} / ${height}` : asset.kind === "video" || asset.kind === "footage" ? "9 / 16" : "1 / 1";
  return <div className="min-w-0 rounded-md border border-border bg-lightbox p-3">
    {!url ? <p className="py-8 text-center text-sm text-muted-foreground">Media unavailable. Try reloading the campaign.</p>
      : <AccessibleMedia src={url} metadataUrl={asset.accessibilityUrl} contentType={asset.kind === "image" ? "image/png" : asset.kind === "voiceover" ? "audio/mpeg" : "video/mp4"} label={assetName(asset)} ratio={ratio} />}
    {asset.durationSeconds !== null && <p className="mt-2 text-xs text-subtle-foreground">{asset.durationSeconds} seconds{asset.width && asset.height ? ` · ${asset.width} × ${asset.height}` : ""}</p>}
  </div>;
}
