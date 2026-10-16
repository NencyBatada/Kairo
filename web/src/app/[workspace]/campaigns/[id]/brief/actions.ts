"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, type FormState } from "@/lib/forms";
import type { ApprovalDecision } from "@/components/approval/approval-panel";

import { readEdit, version, type BriefState } from "./_lib/model";

function failure(error: unknown, status: number): BriefState {
  const state = problemToFormState(error, status);
  return { ...state, message: state.message ?? "Check the highlighted fields and try again." };
}

export async function saveBrief(brandId: string, campaignId: string, form: FormData): Promise<BriefState> {
  const { body, errors } = readEdit(form);
  if (Object.keys(errors).length) return { status: "error", message: "Check the highlighted fields.", fieldErrors: errors };
  let result;
  try { result = await (await coreApi()).PATCH("/api/brands/{brandId}/campaigns/{campaignId}/brief", { params: { path: { brandId, campaignId } }, body }); }
  catch { return { status: "error", message: "Kairo couldn't save the brief. Try again; your edits are still here." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Brief saved. Kairo rechecked the brief and any affected test plan.", detail: result.data };
}

export async function proposeBrief(brandId: string, campaignId: string, form: FormData): Promise<BriefState> {
  const current = version(form.get("version"));
  const briefVersion = version(form.get("briefVersion"), 1);
  const instruction = String(form.get("instruction") ?? "").trim();
  if (current === null || briefVersion === null) return { status: "error", message: "Reload the brief before requesting changes." };
  if (instruction.length < 2 || instruction.length > 500) return { status: "error", fieldErrors: { instruction: ["Describe the change in 2 to 500 characters."] } };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/brief/instructions", { params: { path: { brandId, campaignId } }, body: { version: current, briefVersion, instruction } }); }
  catch { return { status: "error", message: "Kairo couldn't prepare a preview. Try again, or edit the fields directly." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  return { status: "saved", message: "Preview ready. Review the changes before applying them.", proposal: result.data };
}

export async function applyBrief(brandId: string, campaignId: string, proposalId: string, campaignVersion: string): Promise<BriefState> {
  const current = version(campaignVersion);
  if (current === null || !/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(proposalId)) return { status: "error", message: "This preview is invalid. Request a new preview." };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/brief/proposals/{proposalId}/apply", { params: { path: { brandId, campaignId, proposalId } }, body: { version: current } }); }
  catch { return { status: "error", message: "Kairo couldn't apply the changes. Your preview is still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Changes applied. The brief and test plan are being rechecked.", detail: result.data };
}

export async function requestBriefApproval(brandId: string, campaignId: string, campaignVersion: string): Promise<FormState> {
  const current = version(campaignVersion);
  if (current === null) return { status: "error", message: "Reload the campaign before asking for approval." };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/approval-requests", { params: { path: { brandId, campaignId } }, body: { version: current, gate: "brief" } }); }
  catch { return { status: "error", message: "Kairo couldn't request approval. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failure(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Brief sent for approval. Editing is paused until a decision is recorded." };
}

export async function decideBrief(brandId: string, campaignId: string, campaignVersion: string, decision: ApprovalDecision): Promise<FormState> {
  const current = version(campaignVersion);
  if (current === null) return { status: "error", message: "Reload the campaign before deciding." };
  if (decision.kind !== "approve" && decision.kind !== "send-back") return { status: "error", message: "Choose approve or send back." };
  const reason = decision.kind === "send-back" ? decision.reason.trim() : null;
  if (decision.kind === "send-back" && (!reason || reason.length < 2 || reason.length > 1000)) return { status: "error", fieldErrors: { reason: ["Explain what needs to change, in 2 to 1,000 characters."] } };
  let result;
  try {
    const api = await coreApi();
    const params = { path: { brandId, campaignId } };
    result = decision.kind === "approve"
      ? await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/approve", { params, body: { version: current, gate: "brief", comment: null } })
      : await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/send-back", { params, body: { version: current, to: "brief_draft", comment: reason } });
  } catch { return { status: "error", message: "Kairo couldn't record the decision. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) {
    const state = failure(result.error, result.response.status);
    if (state.fieldErrors?.comment) state.fieldErrors = { ...state.fieldErrors, reason: state.fieldErrors.comment };
    return state;
  }
  refresh();
  return { status: "saved", message: decision.kind === "approve" ? "Brief approved. Creative work can start." : "Brief sent back with your comment." };
}
