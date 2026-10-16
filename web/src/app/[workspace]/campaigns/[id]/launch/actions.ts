"use server";

import { refresh } from "next/cache";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState } from "@/lib/forms";
import { version } from "../assets/_lib/edit";
import { readSettings, validateDecision, type Decision, type LaunchState } from "./_lib/model";

function failed(result: { response: Response; error?: unknown }): LaunchState {
  const state = problemToFormState(result.error, result.response.status);
  return { ...state, message: state.message ?? "Check the highlighted fields and try again.", fieldErrors: state.fieldErrors?.comment ? { ...state.fieldErrors, reason: state.fieldErrors.comment } : state.fieldErrors, conflict: result.response.status === 409 };
}

export async function saveLaunchSettings(brandId: string, campaignId: string, expected: number | string, input: unknown): Promise<LaunchState> {
  const current = version(expected);
  if (current === null) return { status: "error", message: "Reload the campaign before saving." };
  let result;
  try {
    const api = await coreApi();
    const params = { path: { brandId, campaignId } };
    const [view, brief, proof] = await Promise.all([
      api.GET("/api/brands/{brandId}/campaigns/{campaignId}/launch-plan", { params }),
      api.GET("/api/brands/{brandId}/campaigns/{campaignId}/brief", { params }),
      api.GET("/api/brands/{brandId}/campaigns/{campaignId}/proof-plan", { params }),
    ]);
    const error = [view, brief, proof].find(value => !value.response.ok);
    if (error) result = error;
    else if (!view.data?.plan || !brief.data || !proof.data) return { status: "error", message: "Complete the brief and test plan before setting up the launch." };
    else if (String(view.data.version) !== String(current) || String(brief.data.campaignVersion) !== String(current)) return { status: "error", conflict: true, message: "The campaign changed. Your edits are kept; reload and review before saving." };
    else {
      const checked = readSettings(input, view.data.plan, brief.data, proof.data);
      if (!checked.settings) return { status: "error", message: "Check the highlighted launch settings.", fieldErrors: checked.errors };
      result = await api.PUT("/api/brands/{brandId}/campaigns/{campaignId}/launch-plan", { params, body: { version: current, settings: checked.settings } });
    }
  } catch { return { status: "error", message: "Kairo couldn't save the launch settings. Your edits are still here; try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failed(result);
  refresh();
  return { status: "saved", message: "Launch settings saved. Review the summary before requesting approval.", view: result.data as LaunchState["view"] };
}

export async function decideLaunch(brandId: string, campaignId: string, expected: number | string, decision: Decision): Promise<LaunchState> {
  const current = version(expected);
  if (current === null || !validateDecision(decision)) return { status: "error", message: "Review the current launch and add a valid decision before continuing." };
  let result;
  try {
    const api = await coreApi();
    const params = { path: { brandId, campaignId } };
    switch (decision.kind) {
      case "request": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/approval-requests", { params, body: { gate: "launch", version: current } }); break;
      case "approve": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/approve", { params, body: { gate: "launch", version: current, comment: null } }); break;
      case "send-back": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/send-back", { params, body: { to: decision.to, comment: decision.reason.trim().replace(/\s+/gu, " "), version: current } }); break;
      case "retry": result = await api.POST("/api/brands/{brandId}/campaigns/{campaignId}/launch/retry", { params, body: { version: current } }); break;
    }
  } catch { return { status: "error", message: "Kairo couldn't record the action. Your comment is kept. Reload the saved progress before trying again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return failed(result);
  refresh();
  return { status: "saved", message: { request: "Launch sent for Gate 3 approval. Launch settings are now read-only.", approve: "Launch approved and queued. Kairo will build and activate the campaigns; they may then spend the approved budget.", "send-back": "Campaign sent back with your comment. Changed work needs fresh approval.", retry: "Launch retry queued. Kairo resumes from saved progress and checks uncertain outcomes before repeating a step." }[decision.kind], ...(decision.kind === "retry" ? { launch: result.data as LaunchState["launch"] } : {}) };
}
