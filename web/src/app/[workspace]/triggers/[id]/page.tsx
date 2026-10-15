import type { Metadata } from "next";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { DeleteRule, RuleControl } from "../_components/rule-controls";
import { RuleForm } from "../_components/rule-form";
import { outcomeLabel } from "../_lib/rules";
import { changeRule, previewOrSaveRule } from "../actions";

export const metadata: Metadata = { title: "Trigger rule" };

export default async function RulePage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const path = { brandId: workspace.id };
  const [detail, brand] = await Promise.all([api.GET("/api/brands/{brandId}/triggers/rules/{ruleId}", { params: { path: { ...path, ruleId: id } } }).then(unwrap), api.GET("/api/brands/{brandId}", { params: { path } }).then(unwrap)]);
  const rule = detail.rule;
  const admin = workspace.roles.includes("admin");
  const edit = admin ? await Promise.all([api.GET("/api/brands/{brandId}/triggers/options", { params: { path } }).then(unwrap), api.GET("/api/brands/{brandId}/triggers/festivals", { params: { path, query: { from: "0001-01-01" } } }).then(unwrap)]) : null;
  const dates = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: brand.timeZone });
  return <div className="flex flex-col gap-8">
    <PageHeader eyebrow="Triggers" title={rule.name} description={rule.description} actions={<Link href={`/${handle}/triggers`} className="text-small text-primary hover:underline">All rules</Link>} />
    <div className="flex flex-wrap items-center gap-3"><Badge variant={rule.status === "active" ? "success" : rule.status === "paused" ? "warning" : "neutral"}>{rule.status === "draft" ? "Suggested" : rule.status === "active" ? "Active" : "Paused"}</Badge><p className="text-small text-muted-foreground">{rule.lastCheckedAt ? `Last checked ${dates.format(new Date(rule.lastCheckedAt))}` : "Not checked yet"}{rule.lastCheckOutcome ? ` · ${outcomeLabel(rule.lastCheckOutcome)}` : ""}</p></div>
    {rule.evidence && <Alert><h2 className="mb-2 font-semibold">Why Kairo suggested this rule</h2><p className="wrap-anywhere">{rule.evidence}</p></Alert>}
    {admin && <div className="flex flex-wrap items-start gap-3">{rule.status === "active" && <RuleControl key={rule.status} label="Pause rule" action={changeRule.bind(null, workspace.id, id, "pause", handle)} />}{rule.status === "paused" && <RuleControl key={rule.status} label="Resume rule" action={changeRule.bind(null, workspace.id, id, "resume", handle)} />}{rule.status === "draft" && <RuleControl key={rule.status} label="Turn on suggested rule" action={changeRule.bind(null, workspace.id, id, "activate", handle)} />}<DeleteRule name={rule.name} action={changeRule.bind(null, workspace.id, id, "delete", handle)} /></div>}
    <Card><CardHeader><CardTitle>Rule results</CardTitle></CardHeader><CardContent><dl className="grid grid-cols-2 gap-4 md:grid-cols-4">{[["Times fired", rule.results.fired], ["Created opportunities", rule.results.created], ["Joined opportunities", rule.results.joined], ["Accepted opportunities", rule.results.accepted]].map(([label, value]) => <div key={label}><dt className="text-small text-muted-foreground">{label}</dt><dd className="mt-1 font-display text-display-md tabular-nums">{value}</dd></div>)}</dl><p className="mt-4 text-small text-muted-foreground">Campaign lift is not available yet. Results will appear after campaigns have been measured.</p></CardContent></Card>
    <section aria-labelledby="firings-title" className="flex flex-col gap-3"><h2 id="firings-title" className="text-base font-semibold">Recent firings</h2>{detail.firings.length ? <ul className="divide-y divide-border rounded-lg border border-border bg-card">{detail.firings.map((firing) => <li key={firing.id} className="flex flex-col gap-2 p-4 text-small"><p className="font-medium wrap-anywhere">{firing.summary}</p><p className="text-muted-foreground">{dates.format(new Date(firing.firedAt))} · {outcomeLabel(firing.outcome)}{firing.endedAt ? " · Condition ended" : ` · Cooldown until ${dates.format(new Date(firing.cooldownUntil))}`}</p>{firing.opportunityId && <Link href={`/${handle}/opportunities/${firing.opportunityId}`} className="inline-flex min-h-8 items-center text-primary hover:underline pointer-coarse:min-h-11">View opportunity{firing.opportunityStatus ? ` · ${firing.opportunityStatus}` : ""}</Link>}</li>)}</ul> : <p className="text-small text-muted-foreground">This rule has not fired yet.</p>}</section>
    {edit && <section aria-labelledby="edit-rule-title" className="flex flex-col gap-5"><h2 id="edit-rule-title" className="text-base font-semibold">Edit rule</h2><RuleForm rule={rule} options={edit[0]} festivals={edit[1]} handle={handle} action={previewOrSaveRule.bind(null, workspace.id, id)} /></section>}
  </div>;
}
