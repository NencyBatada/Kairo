import { ExternalLink, FileSearch, Sparkles } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { Alert } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

import { getRoom } from "../_lib/room";
import { dates } from "../_lib/presentation";
import { evidenceSnapshot } from "../_lib/evidence";

export default async function EvidencePage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const { room, timeZone } = await getRoom(handle, id);
  const evidence = evidenceSnapshot(room.evidence);
  return <section aria-labelledby="evidence-heading" className="flex flex-col gap-5">
    <div><h2 id="evidence-heading" className="text-base font-semibold">Evidence at acceptance</h2><p className="mt-1 text-small text-muted-foreground">Captured when this room opened on {dates(timeZone).format(new Date(room.openedAt))}.</p><Link href={`/${handle}/opportunities/${encodeURIComponent(room.opportunityId)}`} className="mt-2 inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11">Open original opportunity</Link></div>
    {evidence.topic && <p className="wrap-anywhere font-medium">{evidence.topic}</p>}
    {evidence.trigger && <Alert variant="info">{evidence.trigger}</Alert>}
    <div className="grid gap-5 md:grid-cols-2"><Card><CardHeader><CardTitle>Regions</CardTitle></CardHeader><CardContent><p className="text-small wrap-anywhere">{evidence.regions.join(", ") || "No regions were captured."}</p></CardContent></Card><Card><CardHeader><CardTitle>Matching products</CardTitle></CardHeader><CardContent>{evidence.products.length ? <ul className="list-inside list-disc space-y-2 text-small wrap-anywhere">{evidence.products.map((title, index) => <li key={index}>{title}</li>)}</ul> : <p className="text-small text-muted-foreground">No product names were captured.</p>}</CardContent></Card></div>
    {evidence.reasons.length ? <Card><CardHeader><CardTitle>Why demand was rising</CardTitle></CardHeader><CardContent className="flex flex-col gap-4"><p className="flex items-center gap-2 text-small text-muted-foreground"><Sparkles className="size-4" aria-hidden="true" />Drafted by Kairo</p>{evidence.reasons.map((reason, index) => <div key={index} className="rounded-md bg-muted p-4"><p className="wrap-anywhere">{reason.text}</p><div className="mt-3 flex flex-wrap gap-2">{reason.sources.map((source, sourceIndex) => <a key={sourceIndex} href={source.url} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-8 max-w-full items-center gap-2 rounded-full border border-border-strong px-3 py-1 text-small text-primary hover:bg-accent pointer-coarse:min-h-11"><span className="min-w-0 wrap-anywhere">{source.title}</span><ExternalLink className="size-3 shrink-0" aria-hidden="true" /><span className="sr-only">Opens in a new tab</span></a>)}</div>{!reason.sources.length && <p className="mt-2 text-small text-warning-strong">No source supports this captured reason.</p>}</div>)}</CardContent></Card> : <EmptyState icon={FileSearch} title="No sourced explanation was captured" description="Open the original opportunity to check its demand data and sources." />}
  </section>;
}
