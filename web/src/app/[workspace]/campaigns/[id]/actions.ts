"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, type FormState } from "@/lib/forms";

export async function retryRun(brandId: string, campaignId: string, runId: string, _previous: FormState, _form: FormData): Promise<FormState> {
  void _previous; void _form;
  let result;
  try {
    result = await (await coreApi()).POST("/api/brands/{brandId}/campaigns/{campaignId}/stage-runs/{runId}/retry", { params: { path: { brandId, campaignId, runId } } });
  } catch {
    return { status: "error", message: "Kairo couldn't reach the service. Please try again." };
  }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) {
    const state = problemToFormState(result.error, result.response.status);
    return { ...state, message: state.message ?? Object.values(state.fieldErrors ?? {}).flat().join(" ") };
  }
  refresh();
  return { status: "saved", message: "The agent run is queued again." };
}
