"use client";

import { useCallback, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { ApprovalPanel } from "@/components/approval/approval-panel";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { NativeSelect } from "@/components/ui/native-select";
import type { AssetSet } from "../../assets/_lib/model";
import { day, money, type BriefDetail, type ProofResult } from "../../brief/_lib/model";
import { decideLaunch } from "../actions";
import { context, stageNames, type Launch, type LaunchState, type PlanView, type Room } from "../_lib/model";
import { LaunchCommand } from "./launch-command";
import { LaunchProgress } from "./launch-progress";
import { LaunchSettings } from "./launch-settings";
import { LaunchSummary } from "./launch-summary";

export function LaunchWorkspace({ room, view, launch, brief, proof, set, brandId, handle, timeZone, marketer }: { room: Room; view: PlanView; launch: Launch | null; brief: BriefDetail; proof: ProofResult; set: AssetSet; brandId: string; handle: string; timeZone: string; marketer: boolean }) {
  const router = useRouter();
  const [saved, setSaved] = useState<{ source: string; view: PlanView } | null>(null);
  const current = saved?.source === String(view.version) ? saved.view : view;
  const currentContext = context(room, current);
  const [dirty, setDirty] = useState(false);
  const [last, setLast] = useState<{ source: string; state: LaunchState } | null>(null);
  const [returnTo, setReturnTo] = useState("");
  const targets = room.actions.sendBackTo.filter(value => Object.hasOwn(stageNames, value));
  const to = targets.includes(returnTo) ? returnTo : targets[0] ?? "";
  const [localLaunch, setLocalLaunch] = useState<{ source: string; launch: Launch } | null>(null);
  const progress = localLaunch?.source === String(launch?.version) ? localLaunch.launch : launch;
  const busy = last?.source === currentContext && last.state.status === "saved";
  const resultRef = useRef<HTMLDivElement>(null);
  const onDirty = useCallback((value: boolean) => setDirty(value), []);
  function result(state: LaunchState) { setLast({ source: currentContext, state }); if (state.launch) setLocalLaunch({ source: String(launch?.version), launch: state.launch }); if (state.status === "saved") requestAnimationFrame(() => resultRef.current?.focus()); }
  const plan = current.plan;
  const waiting = room.pendingApproval?.gate === "launch" ? room.pendingApproval : null;
  const versionsMatch = String(current.version) === String(room.version) && String(brief.campaignVersion) === String(room.version) && (!proof.plan || Number(proof.plan.version) === Number(plan?.proofPlanVersion));
  const previewsMatch = !!plan && plan.assets.every(asset => set.groups.some(group => group.assets.some(row => row.id === asset.id && String(row.version) === String(asset.version))));
  const blocked = dirty || busy || !versionsMatch;
  const editable = marketer && room.stage === "ready_to_launch" && !waiting && !busy && versionsMatch;
  return <div className="min-w-0 space-y-8"><div className="space-y-3"><div className="flex flex-wrap items-center justify-between gap-3"><h2 className="font-display text-display-sm">Launch</h2><Button variant="secondary" onClick={() => router.refresh()}>Reload launch</Button></div><p className="max-w-prose text-small text-muted-foreground">Review exactly what will go live, approve the spend, then follow the launch here. Progress updates arrive automatically.</p></div>
    <div ref={resultRef} tabIndex={-1} className="outline-none">{last?.state.status === "saved" && <FormStatus state={last.state} />}</div>
    {plan && !versionsMatch && <Alert variant="warning">The campaign is updating. Reload and review the current saved plan before deciding. Any unsaved edits are kept.</Alert>}
    {progress && <LaunchProgress launch={progress} brandId={brandId} campaignId={room.id} timeZone={timeZone} canRetry={marketer} disabled={blocked} onResult={result} />}
    {!plan ? <Alert variant="info">The launch summary appears once the brief and test plan are complete. Finish the brief and approve the assets before requesting Gate 3.</Alert> : <>
      {room.stage !== "ready_to_launch" && !progress && <Alert variant="info">This campaign is not at launch approval yet. Complete the brief and asset approvals first.</Alert>}
      {progress && <p className="text-small text-muted-foreground">The summary below reflects the current saved plan. Any test-plan changes after launch are flagged in the Brief tab. Saved launch progress and IDs above remain the record of execution.</p>}
      <LaunchSummary view={current} set={set} brief={brief} brandId={brandId} campaignId={room.id} handle={handle} />
      <LaunchSettings view={current} brief={brief} proof={proof} brandId={brandId} campaignId={room.id} editable={editable} onDirty={onDirty} onSaved={next => setSaved({ source: String(view.version), view: next })} />
      {room.stage === "ready_to_launch" && <section aria-label="Launch approval" className="space-y-4">
        {waiting ? <>
          {targets.length > 0 && <div className="max-w-sm space-y-2"><label htmlFor="launch-send-back-target" className="text-small font-semibold">Send back to</label><NativeSelect id="launch-send-back-target" value={to} onChange={event => setReturnTo(event.target.value)} disabled={blocked}>{targets.map(target => <option key={target} value={target}>{stageNames[target]}</option>)}</NativeSelect></div>}
          <ApprovalPanel label="Gate 3 · Launch" title="Approve the launch" subject="launch" status="waiting" approver={waiting.approvers.map(person => person.name).join(", ") || "No approver assigned"} canDecide={room.actions.approve === "launch" || targets.length > 0} approveDisabled={blocked || room.actions.approve !== "launch" || !!current.blocker || !previewsMatch} contextKey={`${currentContext}:${to}:${previewsMatch}`} approveLabel="Approve and launch" consequence={`This approves the exact saved plan: ${money(plan.budget, plan.currency)} from ${day(plan.start)} to ${day(plan.end)}. Kairo will upload videos as unlisted, build paused Google Ads campaigns excluding holdout regions, then activate all campaign types together. Campaigns may then spend the approved budget. Your name and the approved plan are recorded.`} facts={[{ label: "Total approved spend", value: money(plan.budget, plan.currency) }, { label: "Holdouts excluded", value: plan.holdouts.map(region => region.name).join(", ") }, { label: "Assets in this plan", value: String(plan.assets.length) }]} sendBack={{ label: "Send back", confirmLabel: "Send campaign back", description: `Return the campaign to ${stageNames[to] ?? "the selected stage"} with your comment. Later approvals are invalidated and changed work needs fresh approval.`, disabled: blocked || !targets.length, summary: <p className="text-small font-semibold">Return to: {stageNames[to]}</p> }} onDecision={async decision => { const state = await decideLaunch(brandId, room.id, room.version, decision.kind === "send-back" ? { kind: "send-back", to, reason: decision.reason } : { kind: "approve" }); result(state); return state; }}>
            {waiting.routeReason && <Alert variant="warning">{waiting.routeReason}</Alert>}{current.blocker && <Alert variant="warning">{current.blocker}</Alert>}{!previewsMatch && <Alert variant="warning">Some saved asset versions cannot be previewed. Reload and review them before approving.</Alert>}{blocked && <p className="text-small text-warning-strong">Finish unsaved work and review the latest saved version before deciding.</p>}
          </ApprovalPanel>
        </> : <div className="space-y-4 rounded-lg border bg-card p-5 sm:p-6"><Badge variant="gate">Gate 3 · Launch</Badge><h2 className="text-xl font-semibold">Request launch approval</h2>{(current.blocker || room.actions.requestApprovalBlocker) && <Alert variant="warning">{current.blocker ?? room.actions.requestApprovalBlocker}</Alert>}{dirty && <p className="text-small text-warning-strong">Save or discard launch settings before requesting approval.</p>}{marketer ? <LaunchCommand label="Send launch for approval" description="The saved launch plan is sent to its assigned approvers, or a budget owner when required. Launch settings become read-only until a decision is recorded. This request does not start the launch." contextKey={currentContext} disabled={blocked || room.actions.requestApproval !== "launch" || !!current.blocker || !previewsMatch} run={async () => { const state = await decideLaunch(brandId, room.id, room.version, { kind: "request" }); result(state); return state; }} /> : <p className="text-small text-muted-foreground">A marketer can request approval once the saved launch plan is ready.</p>}</div>}
      </section>}
    </>}
    {room.stage === "launching" && progress?.status === "failed" && !progress.uncertain && targets.length > 0 && <section aria-labelledby="return-launch-title" className="space-y-4 rounded-lg border bg-card p-5"><h2 id="return-launch-title" className="text-lg font-semibold">Change the failed launch</h2><p className="text-small text-muted-foreground">Use this when the plan needs changes. The service checks that any created campaigns are paused or removed before returning the campaign. Changed work needs fresh approval.</p><div className="space-y-2"><label htmlFor="failed-launch-return-target" className="block text-small font-semibold">Return to</label><NativeSelect id="failed-launch-return-target" value={to} onChange={event => setReturnTo(event.target.value)} disabled={blocked}>{targets.map(target => <option key={target} value={target}>{stageNames[target]}</option>)}</NativeSelect></div><LaunchCommand label="Return campaign for changes" description={`Return to ${stageNames[to]}. Your comment is recorded, this launch is retired and affected approvals must be requested again.`} contextKey={`${currentContext}:${JSON.stringify(progress)}:${to}`} disabled={blocked} comment run={async reason => { const state = await decideLaunch(brandId, room.id, room.version, { kind: "send-back", to, reason }); result(state); return state; }} /></section>}
  </div>;
}
