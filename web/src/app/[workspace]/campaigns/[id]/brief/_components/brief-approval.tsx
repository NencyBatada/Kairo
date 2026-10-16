"use client";

import { useActionState, useId } from "react";

import { ApprovalPanel } from "@/components/approval/approval-panel";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";
import { decideBrief, requestBriefApproval } from "../actions";
import { day, money, type BriefDetail, type ProofResult } from "../_lib/model";

export function BriefApproval({ room, detail, proof, brandId, disabled, canRequest }: { room: components["schemas"]["CampaignDetail"]; detail: BriefDetail; proof: ProofResult; brandId: string; disabled: boolean; canRequest: boolean }) {
  const id = useId();
  const [state, action, pending] = useActionState<FormState, FormData>(async () => requestBriefApproval(brandId, room.id, String(room.version)), { status: "idle" });
  const brief = detail.brief!;
  const approval = room.pendingApproval?.gate === "brief" ? room.pendingApproval : null;
  const budget = brief.budget;
  const cap = detail.budgetLimit;
  const overLimit = budget && cap && (budget.currency !== cap.currency || Number(budget.amount) > Number(cap.amount));
  const facts = [
    { label: "Budget", value: budget ? money(budget.amount, budget.currency) : "Not set" },
    { label: "Per-campaign limit", value: cap ? money(cap.amount, cap.currency) : "No limit set" },
    { label: "Dates", value: `${day(brief.startDate)} to ${day(brief.endDate)}` },
    { label: "Regions", value: brief.regions.join(", ") || "Not set" },
    { label: "Holdouts", value: proof.plan?.holdouts.map(region => region.name).join(", ") || "Not ready" },
  ];
  if (approval) return <section id="brief-approval" aria-label="Brief approval"><ApprovalPanel key={approval.requestedAt} label="Gate 1 · Brief" title="Review the brief" subject="brief" status="waiting" approver={approval.approvers.map(person => person.name).join(", ") || "No approver assigned"} canDecide={!disabled && room.actions.approve === "brief"} facts={facts} approveLabel="Approve brief" consequence="This approves the saved brief and its test plan. Creative work starts next. Any data warnings are recorded with your approval." onDecision={decision => decideBrief(brandId, room.id, String(room.version), decision)}>
    {approval.routeReason && <Alert variant="warning"><p>{approval.routeReason}</p></Alert>}
    {proof.plan?.warnings.length ? <Alert variant="warning"><p>This test plan has data warnings. Review them above; approving records that you accepted these limits.</p></Alert> : null}
    {disabled && <p className="text-small text-muted-foreground">Finish or discard unsaved work before deciding.</p>}
  </ApprovalPanel></section>;
  return <section id="brief-approval" aria-labelledby={`${id}-title`} className="space-y-4 rounded-lg border bg-card p-5 sm:p-6"><Badge variant="gate">Gate 1 · Brief</Badge><h2 id={`${id}-title`} className="text-xl font-semibold">Brief approval</h2>
    {room.stage !== "brief_draft" ? <p className="text-small text-muted-foreground">This campaign has moved beyond brief review. The brief is read-only at this stage.</p> : <>
      {overLimit && <Alert variant="warning"><p>{budget!.currency !== cap!.currency ? "The budget and limit use different currencies." : "The budget is above the per-campaign limit."} A budget owner must approve this brief. The approval request will name the people who can decide.</p></Alert>}
      {room.actions.requestApprovalBlocker && <Alert variant="warning"><p>{room.actions.requestApprovalBlocker}</p></Alert>}
      {canRequest ? <form action={action} className="space-y-3"><p className="text-small text-muted-foreground">Sending the brief pauses editing while the approver reviews it.</p>{disabled && <p className="text-small text-warning-strong">Save or discard your edits and finish the preview before asking for approval.</p>}<Button type="submit" disabled={disabled || room.actions.requestApproval !== "brief" || state.status === "saved"} pending={pending} className="w-full sm:w-auto">Send for approval</Button><FormStatus state={state} /></form> : <p className="text-small text-muted-foreground">A marketer can send this brief for approval once its checks and test plan are ready.</p>}
    </>}
  </section>;
}
