import { Eye, EyeOff } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { AddTopic, RemoveTopic } from "./_components/topic-controls";
import { addTopic, removeTopic } from "./actions";

export const metadata: Metadata = { title: "Watched and ignored topics" };

export default async function TopicsPage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const topics = await unwrap(await (await coreApi()).GET("/api/brands/{brandId}/opportunities/topics", { params: { path: { brandId: workspace.id } } }));
  const canManage = workspace.roles.some((role) => role === "admin" || role === "marketer");
  return <div className="flex flex-col gap-8">
    <PageHeader eyebrow="Opportunity Radar" title="Watched and ignored topics" description="Choose which demand Kairo keeps in view for your brand." actions={<Link href={`/${handle}/opportunities`} className="text-small text-primary hover:underline">All opportunities</Link>} />
    <Alert variant="info">Watched topics can appear below the usual score cut-off when matching products are in stock. Ignored topics stay out of your lists. A topic can only have one preference.</Alert>
    {canManage ? <Card><CardHeader><CardTitle>Add a topic</CardTitle></CardHeader><CardContent><AddTopic action={addTopic.bind(null, workspace.id)} /><p className="mt-4 text-small text-muted-foreground">Use 2 to 100 characters. Your brand can keep up to 200 topics. To change a preference, remove the topic and add it again.</p></CardContent></Card> : <Alert>Only workspace admins and marketers can manage topics.</Alert>}
    <div className="grid items-start gap-5 xl:grid-cols-2">{([{ kind: "watch", title: "Watched topics", Icon: Eye, empty: "No watched topics yet. Add a topic to keep its matching opportunities in view." }, { kind: "ignore", title: "Ignored topics", Icon: EyeOff, empty: "No ignored topics yet. Add a topic to hide its matching opportunities." }] as const).map(({ kind, title, Icon, empty }) => {
      const items = topics.filter((topic) => topic.kind === kind);
      return <Card key={kind}><CardHeader><CardTitle className="flex items-center gap-2"><Icon aria-hidden="true" className="size-4" />{title} · {items.length}</CardTitle></CardHeader><CardContent>{items.length ? <ul aria-label={title} className="divide-y divide-border">{items.map((topic) => <li key={topic.id} className="flex min-w-0 flex-wrap items-center justify-between gap-3 py-3"><span className="min-w-0 flex-1 text-sm wrap-anywhere">{topic.phrase}</span>{canManage && <RemoveTopic phrase={topic.phrase} kind={kind} action={removeTopic.bind(null, workspace.id, topic.id)} />}</li>)}</ul> : <p className="text-small text-muted-foreground">{empty}</p>}</CardContent></Card>;
    })}</div>
  </div>;
}
