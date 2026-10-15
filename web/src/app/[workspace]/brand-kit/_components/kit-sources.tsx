import { ExternalLink } from "lucide-react";

import type { KitSource } from "./types";

export function KitSources({ sources = [] }: { sources?: KitSource[] }) {
  if (!sources.length) return null;
  return <ul aria-label="Sources" className="flex flex-wrap gap-2">
    {sources.map((source, i) => {
      let url: URL | undefined;
      try { url = new URL(source.ref); } catch { url = undefined; }
      return <li key={i} className="min-w-0 text-small">
        {source.kind === "page" && url && ["https:", "http:"].includes(url.protocol) ? (
          <a href={url.href} target="_blank" rel="noopener noreferrer" className="inline-flex max-w-full items-center gap-1 rounded-full border border-border-strong px-3 py-1 text-primary hover:underline">
            <span className="truncate">{url.hostname}{url.pathname}</span><ExternalLink className="size-3.5 shrink-0" aria-hidden="true" />
          </a>
        ) : <span className="text-muted-foreground">{source.kind === "ad" ? `Google Ads ad ${source.ref}` : "Source unavailable"}</span>}
      </li>;
    })}
  </ul>;
}
