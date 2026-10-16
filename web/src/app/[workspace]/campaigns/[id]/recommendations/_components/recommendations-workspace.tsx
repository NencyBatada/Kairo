"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Lightbulb } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { date, type CareState, type Member, type Recommendation, type Recommendations, type Room, type Settings } from "../_lib/model";
import { RecommendationCard } from "./recommendation-card";
import { EndTest } from "./end-test";

export function RecommendationsWorkspace({ list, room, brandId, handle, currency, timeZone, settings, members, marketer, canDismiss }: { list: Recommendations; room: Room; brandId: string; handle: string; currency: string; timeZone: string; settings: Settings; members: Member[]; marketer: boolean; canDismiss: boolean }) {
  const router = useRouter(), resultRef = useRef<HTMLDivElement>(null);
  const [result, setResult] = useState<CareState>({ status: "idle" });
  const [busy, onBusy] = useState(false);
  const [saved, save] = useState<Record<string, { base: string; value: Recommendation }>>({});
  const [ended, setEnded] = useState<{ base: string; value: Room } | null>(null);
  const [filter, setFilter] = useState("all");
  const currentRoom = ended?.base === JSON.stringify(room) ? ended.value : room;
  const test = ended?.base === JSON.stringify(room) ? { ...list.test, running: false, endedEarly: true, stage: currentRoom.stage } : list.test;
  const recommendations = list.recommendations.map(rec => saved[rec.id]?.base === JSON.stringify(rec) ? saved[rec.id].value : rec);
  const filtered = recommendations.filter(rec => filter === "all" || (filter === "held" ? rec.held : rec.state === filter && !rec.held));
  const receive = (state: CareState) => { setResult(state); if (state.recommendation) { const value = state.recommendation; save(existing => ({ ...existing, [value.id]: { base: JSON.stringify(list.recommendations.find(rec => rec.id === value.id)), value } })); } if (state.room) setEnded({ base: JSON.stringify(room), value: state.room }); };
  useEffect(() => { if (result.status !== "idle") resultRef.current?.focus(); }, [result]);
  return <div className="min-w-0 space-y-8">
    <section aria-labelledby="recommendations-title" className="space-y-5"><div className="flex flex-wrap items-start justify-between gap-4"><div className="space-y-2"><h2 id="recommendations-title" className="text-base font-semibold">Recommendations</h2><p className="max-w-prose text-small text-muted-foreground">Review the expected effect and saved evidence. One approval applies a recommendation directly in Google Ads.</p></div><Button variant="secondary" disabled={busy} onClick={() => router.refresh()}>Reload recommendations</Button></div>
      <div className="space-y-3 rounded-lg border border-border bg-card p-4"><Badge variant={test.running ? "info" : "neutral"}>{test.running ? "Test running" : test.endedEarly ? "Test ended early" : ["results_ready", "closed"].includes(test.stage) ? "Test ended" : "Test not running"}</Badge><p className="text-small text-muted-foreground">{test.start && test.end ? `Planned test: ${date(test.start)} to ${date(test.end)}.` : "No proof-plan dates are available yet."} {test.running ? "Kairo’s budget and target recommendations stay held. Google Ads suggestions shown here have passed the service’s test protections." : "Kairo checks for eligible post-test recommendations on its scheduled run. An empty list does not mean the test proved a result."}</p></div>
      <Alert variant="neutral">Expected effects are estimates. During a test, Google Ads suggestions that could reach holdout regions (areas left out so we can measure lift) are filtered by the service.</Alert>
      {result.status !== "idle" && <div role={result.status === "error" ? "alert" : "status"} ref={resultRef} tabIndex={-1} className="rounded-lg border border-border bg-muted p-4 text-small">{result.message}</div>}
      <label htmlFor="recommendation-filter" className="block text-small font-medium">Show recommendations</label><select id="recommendation-filter" value={filter} onChange={event => setFilter(event.target.value)} className="min-h-11 w-full rounded-md border border-input bg-card px-3 text-base sm:w-64">{[["all", "All recommendations"], ["proposed", "Ready for review"], ["held", "Held during test"], ["approved", "Application in progress"], ["applied", "Applied"], ["dismissed", "Dismissed"], ["expired", "Expired"]].map(([value, name]) => <option key={value} value={value}>{name}</option>)}</select>
      {!recommendations.length ? <EmptyState icon={Lightbulb} title="No recommendations yet" description="Eligible suggestions appear after the scheduled check. Kairo’s budget and target changes wait for the test to end." /> : !filtered.length ? <p className="rounded-lg border border-border bg-card p-5 text-small text-muted-foreground">No recommendations match this filter.</p> : <div className="divide-y divide-border rounded-xl border border-border bg-card">{filtered.map(rec => <RecommendationCard key={rec.id} recommendation={rec} test={test} roomVersion={currentRoom.version} settings={settings} members={members} currency={currency} timeZone={timeZone} brandId={brandId} canDismiss={canDismiss} onResult={receive} busy={busy} onBusy={onBusy} />)}</div>}
    </section>
    <EndTest room={currentRoom} test={test} brandId={brandId} canEnd={marketer && currentRoom.stage === "live" && test.running} onResult={receive} busy={busy} onBusy={onBusy} />
    <Link href={`/${handle}/campaigns/${room.id}/performance`} className="inline-flex min-h-11 items-center text-small text-primary hover:underline">Back to performance</Link>
  </div>;
}
