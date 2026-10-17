import Link from "next/link";
import { Sparkles } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { MemoryEditor } from "../_components/memory-editor";
import { MemoryEvidence } from "../_components/memory-evidence";
import { kindName, languageName, outcomeName, sourceAnalysis, stamp, usageName } from "../_lib/model";

export default async function MemoryDetailPage({ params }: { params: Promise<{ workspace: string; entryId: string }> }) {
  const { workspace: handle, entryId } = await params, workspace = await getWorkspace(handle), api = await coreApi();
  const path = { brandId: workspace.id };
  const [detail, brand, members] = await Promise.all([
    api.GET("/api/brands/{brandId}/brand-memory/{entryId}", { params: { path: { ...path, entryId } } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: { path } }).then(unwrap),
    api.GET("/api/brands/{brandId}/members", { params: { path } }).then(unwrap),
  ]);
  const entry = detail.entry, names = Object.fromEntries(members.map(member => [member.userId, member.name])), name = (id: string | null) => id === "learn" ? "Learn agent" : id ? names[id] ?? "Former or unavailable member" : "Not recorded";
  return <div className="min-w-0 space-y-8"><div className="[&_h1]:wrap-anywhere"><PageHeader title={entry.title} eyebrow="Brand Memory" description="The lesson, its supporting evidence and its recorded uses." actions={<Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/brand-kit/memory`}>All memory entries</Link>} /></div>
    <Card><CardHeader><CardTitle>Saved lesson</CardTitle></CardHeader><CardContent className="space-y-5"><div className="flex flex-wrap gap-2">{entry.createdBy === "learn" && !entry.editedAt && <Badge variant="neutral" className="max-w-full whitespace-normal"><Sparkles className="size-3" aria-hidden="true" />Drafted by Kairo</Badge>}<Badge variant="neutral">{kindName(entry.kind)}</Badge><Badge variant="neutral">{outcomeName(entry.outcome)}</Badge><Badge variant="neutral">{entry.state === "removed" ? "Removed" : "Active"}</Badge></div><p className="max-w-prose text-base whitespace-pre-wrap wrap-anywhere">{entry.lesson}</p><dl className="grid gap-4 text-small sm:grid-cols-2">{[["Products", entry.products], ["Categories", entry.categories], ["Regions", entry.regions], ["Languages", entry.languages.map(languageName)]].map(([title, values]) => <div key={String(title)}><dt className="font-medium">{title}</dt><dd className="mt-1 wrap-anywhere text-muted-foreground">{(values as string[]).join(", ") || "Not specified"}</dd></div>)}</dl>{entry.sourceCampaignId && <Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/campaigns/${entry.sourceCampaignId}/results`}>View source campaign results</Link>}<dl className="space-y-2 text-small text-muted-foreground"><div><dt className="inline font-medium">Created: </dt><dd className="inline">{stamp(entry.createdAt, brand.timeZone)} by {name(entry.createdBy)}</dd></div>{entry.editedAt && <div><dt className="inline font-medium">Edited: </dt><dd className="inline">{stamp(entry.editedAt, brand.timeZone)} by {name(entry.editedBy)}</dd></div>}{entry.removedAt && <div><dt className="inline font-medium">Removed: </dt><dd className="inline">{stamp(entry.removedAt, brand.timeZone)} by {name(entry.removedBy)}</dd></div>}</dl></CardContent></Card>
    <Card><CardHeader><CardTitle>Supporting evidence</CardTitle></CardHeader><CardContent className="space-y-4">{sourceAnalysis(entry) && <Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/impact-lab/${sourceAnalysis(entry)}`}>View source Impact Lab analysis</Link>}<MemoryEvidence value={entry.evidence} /></CardContent></Card>
    <MemoryEditor key={entry.id} detail={detail} brandId={workspace.id} canEdit={workspace.roles.some(role => role === "admin" || role === "marketer")} />
    <Card><CardHeader><CardTitle>Where this lesson was used</CardTitle></CardHeader><CardContent className="space-y-3"><p className="text-small text-muted-foreground">{entry.uses} recorded uses. Showing up to the latest 100 uses.</p>{detail.uses.length ? <ul className="divide-y">{detail.uses.map((use, index) => <li key={index} className="space-y-1 py-3 text-small"><p>{usageName(use.usedIn)} · {stamp(use.usedAt, brand.timeZone)}</p>{use.campaignId ? <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/${handle}/campaigns/${use.campaignId}/${use.usedIn === "asset" ? "assets" : "brief"}`}>View campaign {usageName(use.usedIn).toLowerCase()}</Link> : use.usedIn === "opportunity" ? <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/${handle}/opportunities/${use.targetId}`}>View opportunity</Link> : <p className="text-muted-foreground">Campaign link unavailable.</p>}</li>)}</ul> : <p className="text-small text-muted-foreground">No recorded uses returned for this entry.</p>}</CardContent></Card>
  </div>;
}
