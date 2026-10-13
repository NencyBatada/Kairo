"use server";

import { refresh } from "next/cache";
import { redirect } from "next/navigation";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function createWorkspace(_: FormState, formData: FormData): Promise<FormState> {
  const { data, error, response } = await (await coreApi()).POST("/api/brands", {
    body: {
      name: text(formData, "name"),
      handle: text(formData, "handle").toLowerCase(),
      timeZone: text(formData, "timeZone"),
      currency: text(formData, "currency"),
    },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  if (!data) {
    return problemToFormState(error, response.status);
  }

  redirect(`/${data.handle}/today`);
}

export async function acceptInvitation(invitationId: string) {
  const { data, response } = await (await coreApi()).POST("/api/me/invitations/{invitationId}/accept", {
    params: { path: { invitationId } },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  if (!data) {
    refresh();
    return;
  }

  redirect(`/${data.handle}/today`);
}

export async function declineInvitation(invitationId: string) {
  const { response } = await (await coreApi()).POST("/api/me/invitations/{invitationId}/decline", {
    params: { path: { invitationId } },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  refresh();
}
