"use server";

import { refresh, revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, type FormState } from "@/lib/forms";
import { readRule, type RuleFormState } from "./_lib/rules";

export async function previewOrSaveRule(brandId: string, ruleId: string | null, previous: RuleFormState, form: FormData): Promise<RuleFormState> {
  const { request, errors } = readRule(form);
  if (Object.keys(errors).length) return { status: "error", fieldErrors: errors };
  const save = form.get("intent") === "save";
  if (save && JSON.stringify(previous.preview?.request) !== JSON.stringify(request)) return { status: "error", message: "Preview this version of the rule before saving." };
  let result;
  try {
    const api = await coreApi();
    if (!save) result = await api.POST("/api/brands/{brandId}/triggers/rules/preview", { params: { path: { brandId } }, body: request });
    else if (ruleId) result = await api.PUT("/api/brands/{brandId}/triggers/rules/{ruleId}", { params: { path: { brandId, ruleId } }, body: request });
    else result = await api.POST("/api/brands/{brandId}/triggers/rules", { params: { path: { brandId } }, body: request });
  } catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), ...(save ? { preview: previous.preview } : {}) };
  if (!save && result.data && "description" in result.data) return { status: "idle", preview: { request, description: result.data.description } };
  refresh();
  return { status: "saved", message: ruleId ? "Rule updated." : "Rule created and active." };
}

export async function changeRule(brandId: string, ruleId: string, operation: "pause" | "resume" | "activate" | "delete", handle: string, _previous: FormState, _form: FormData): Promise<FormState> {
  void _previous; void _form;
  let result;
  try {
    const api = await coreApi();
    const params = { path: { brandId, ruleId } };
    if (operation === "delete") result = await api.DELETE("/api/brands/{brandId}/triggers/rules/{ruleId}", { params });
    else if (operation === "pause") result = await api.POST("/api/brands/{brandId}/triggers/rules/{ruleId}/pause", { params });
    else if (operation === "resume") result = await api.POST("/api/brands/{brandId}/triggers/rules/{ruleId}/resume", { params });
    else if (operation === "activate") result = await api.POST("/api/brands/{brandId}/triggers/rules/{ruleId}/activate", { params });
    else return { status: "error", message: "Choose a valid rule action." };
  } catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  if (operation === "delete") {
    revalidatePath(`/${encodeURIComponent(handle)}/triggers`);
    redirect(`/${encodeURIComponent(handle)}/triggers`);
  }
  refresh();
  return { status: "saved", message: ({ pause: "Rule paused.", resume: "Rule resumed.", activate: "Suggested rule is now active.", delete: "Rule deleted." })[operation] };
}
