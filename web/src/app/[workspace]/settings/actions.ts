"use server";

import { refresh } from "next/cache";
import { redirect } from "next/navigation";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { getMe } from "@/lib/core-api/queries";
import type { Role } from "@/lib/core-api/types";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function updateWorkspace(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).PATCH("/api/brands/{brandId}", {
    params: { path: { brandId } },
    body: { name: text(formData, "name"), timeZone: text(formData, "timeZone") },
  });
  return finish(error, response, "Saved.");
}

export async function inviteMember(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).POST("/api/brands/{brandId}/invitations", {
    params: { path: { brandId } },
    body: { email: text(formData, "email"), roles: roles(formData) },
  });
  return finish(error, response, "Invitation sent. Ask them to sign in to Kairo with that email.");
}

export async function revokeInvitation(brandId: string, invitationId: string) {
  const { response } = await (await coreApi()).POST("/api/brands/{brandId}/invitations/{invitationId}/revoke", {
    params: { path: { brandId, invitationId } },
  });
  if (response.status === 401) {
    return signInAgain();
  }

  refresh();
}

export async function changeRoles(brandId: string, userId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).PUT("/api/brands/{brandId}/members/{userId}/roles", {
    params: { path: { brandId, userId } },
    body: { roles: roles(formData) },
  });
  return finish(error, response, "Roles saved.");
}

export async function removeMember(brandId: string, userId: string): Promise<FormState> {
  const { error, response } = await (await coreApi()).DELETE("/api/brands/{brandId}/members/{userId}", {
    params: { path: { brandId, userId } },
  });
  if (response.ok && (await getMe()).id === userId) {
    redirect("/");
  }

  return finish(error, response, "Removed.");
}

export async function saveApprovalSettings(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const limit = text(formData, "budgetLimit");
  if (limit !== "" && !Number.isFinite(Number(limit))) {
    return { status: "error", fieldErrors: { budgetLimit: ["Enter a number, or leave it empty."] } };
  }

  const { error, response } = await (await coreApi()).PUT("/api/brands/{brandId}/approval-settings", {
    params: { path: { brandId } },
    body: {
      briefApprovers: ids(formData, "briefApprovers"),
      assetApprovers: ids(formData, "assetApprovers"),
      launchApprovers: ids(formData, "launchApprovers"),
      budgetOwners: ids(formData, "budgetOwners"),
      budgetMoveApprovers: ids(formData, "budgetMoveApprovers"),
      targetChangeApprovers: ids(formData, "targetChangeApprovers"),
      googleSuggestionApprovers: ids(formData, "googleSuggestionApprovers"),
      budgetLimit: limit === "" ? null : Number(limit),
    },
  });
  return finish(error, response, "Approval settings saved.");
}

async function finish(error: unknown, response: Response, message: string): Promise<FormState> {
  if (response.status === 401) {
    return signInAgain();
  }

  if (!response.ok) {
    return problemToFormState(error, response.status);
  }

  refresh();
  return { status: "saved", message };
}

function roles(formData: FormData) {
  return formData.getAll("roles").map(String) as Role[];
}

function ids(formData: FormData, name: string) {
  return formData.getAll(name).map(String);
}
