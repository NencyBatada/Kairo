"use server";

import { coreApi } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { setupErrors, validId, validVersion, type LabState, type Setup, type Selection, type Operation } from "./_lib/model";

export async function createAnalysis(brandId: string, setup: Setup): Promise<LabState> {
  if (!validId(brandId)) return { status: "error", message: "Reload this workspace before creating an analysis." };
  const errors = setupErrors(setup);
  if (Object.keys(errors).length) return { status: "error", fieldErrors: errors };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/impact-lab/analyses", { params: { path: { brandId } }, body: { ...setup, version: null } }); }
  catch { return { status: "error", conflict: true, message: "Kairo couldn't confirm creation. Check the analysis history before creating another analysis. Your draft is still here." }; }
  return answer(result);
}

export async function changeAnalysis(brandId: string, analysisId: string, version: number, operation: Operation, payload?: Setup | Selection): Promise<LabState> {
  if (!validId(brandId) || !validId(analysisId) || !validVersion(version)) return { status: "error", message: "Reload this analysis before continuing." };
  if (operation === "setup") {
    if (!payload || !("name" in payload)) return { status: "error", message: "Complete the setup before saving." };
    const errors = setupErrors(payload);
    if (Object.keys(errors).length) return { status: "error", fieldErrors: errors };
  }
  const params = { path: { brandId, analysisId } }, body = { version };
  let result;
  try {
    const api = await coreApi();
    switch (operation) {
      case "setup": result = await api.PUT("/api/brands/{brandId}/impact-lab/analyses/{analysisId}", { params, body: { ...payload as Setup, version } }); break;
      case "selection": {
        if (!payload || !("affected" in payload) || !Array.isArray(payload.affected) || !payload.affected.length || payload.affected.length > 50 || payload.affected.some(key => typeof key !== "string") || payload.comparison !== null && (!Array.isArray(payload.comparison) || payload.comparison.length > 50 || payload.comparison.some(key => typeof key !== "string" || payload.affected?.includes(key)))) return { status: "error", message: "Review the affected and comparison series." };
        result = await api.PUT("/api/brands/{brandId}/impact-lab/analyses/{analysisId}/selection", { params, body: { ...payload, version } }); break;
      }
      case "load": result = await api.POST("/api/brands/{brandId}/impact-lab/analyses/{analysisId}/load", { params, body }); break;
      case "run": result = await api.POST("/api/brands/{brandId}/impact-lab/analyses/{analysisId}/run", { params, body }); break;
      case "memory": result = await api.POST("/api/brands/{brandId}/impact-lab/analyses/{analysisId}/memory", { params, body }); break;
      default: return { status: "error", message: "Choose an available analysis action." };
    }
  } catch { return { status: "error", conflict: true, message: "Kairo couldn't confirm the change. Your draft is still here. Refresh and review the current version before trying again." }; }
  return answer(result);
}

function answer(result: { data?: unknown; error?: unknown; response: Response }): LabState {
  if (result.response.status === 401) return { status: "error", signIn: true, message: "Your session expired. Sign in again to continue; your draft is still here." };
  if (result.response.status >= 500) return { status: "error", conflict: true, message: "Kairo couldn't finish this request. Refresh and review the saved analysis before retrying." };
  if (!result.response.ok) return { ...problemToFormState(result.error, result.response.status), conflict: result.response.status === 409 };
  const analysis = result.data as LabState["analysis"];
  if (!analysis || !validId(analysis.id) || !validVersion(analysis.version)) return { status: "error", conflict: true, message: "The response could not be confirmed. Refresh the analysis before continuing." };
  return { status: "saved", analysis, message: "Analysis updated." };
}
