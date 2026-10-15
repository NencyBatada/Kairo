"use server";

import { refresh } from "next/cache";
import { redirect } from "next/navigation";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export type DecisionState = FormState & { opportunityStatus?: string };
type Result = { response: Response; data?: unknown; error?: unknown };

async function decide(request: () => Promise<Result>): Promise<DecisionState & { roomId?: string }> {
  let result: Result;
  try { result = await request(); }
  catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  const data = result.data as { roomId?: string; status?: string } | undefined;
  refresh();
  return { status: "saved", roomId: data?.roomId, opportunityStatus: data?.status };
}

export async function acceptOpportunity(brandId: string, opportunityId: string, handle: string, _previous: DecisionState, _form: FormData): Promise<DecisionState> {
  void _previous; void _form;
  const result = await decide(async () => (await coreApi()).POST("/api/brands/{brandId}/opportunities/{opportunityId}/accept", { params: { path: { brandId, opportunityId } } }));
  if (result.status === "saved" && result.roomId) redirect(`/${encodeURIComponent(handle)}/campaigns/${encodeURIComponent(result.roomId)}`);
  return result.status === "saved" ? { status: "error", message: "The campaign room was created, but Kairo couldn't read its link. Reload this opportunity to open it." } : result;
}

export async function saveOpportunity(brandId: string, opportunityId: string, _previous: DecisionState, _form: FormData): Promise<DecisionState> {
  void _previous; void _form;
  const result = await decide(async () => (await coreApi()).POST("/api/brands/{brandId}/opportunities/{opportunityId}/save", { params: { path: { brandId, opportunityId } } }));
  return result.status === "saved" ? { ...result, message: "Opportunity saved.", opportunityStatus: "saved" } : result;
}

export async function unsaveOpportunity(brandId: string, opportunityId: string, _previous: DecisionState, _form: FormData): Promise<DecisionState> {
  void _previous; void _form;
  const result = await decide(async () => (await coreApi()).POST("/api/brands/{brandId}/opportunities/{opportunityId}/unsave", { params: { path: { brandId, opportunityId } } }));
  return result.status === "saved" ? { ...result, message: "Opportunity removed from Saved.", opportunityStatus: "new" } : result;
}

export async function dismissOpportunity(brandId: string, opportunityId: string, _previous: DecisionState, form: FormData): Promise<DecisionState> {
  void _previous;
  const reason = text(form, "reason");
  const note = text(form, "note");
  if (!["not_relevant", "too_small", "wrong_timing", "already_covered", "other"].includes(reason)) return { status: "error", fieldErrors: { reason: ["Choose a reason."] } };
  if (reason === "other" && !note) return { status: "error", fieldErrors: { note: ["Say why you're dismissing it."] } };
  if (note.length > 500) return { status: "error", fieldErrors: { note: ["Use up to 500 characters."] } };
  const result = await decide(async () => (await coreApi()).POST("/api/brands/{brandId}/opportunities/{opportunityId}/dismiss", { params: { path: { brandId, opportunityId } }, body: { reason, note: note || null } }));
  return result.status === "saved" ? { ...result, message: "Opportunity dismissed. Your feedback will help rank future opportunities.", opportunityStatus: "dismissed" } : result;
}
