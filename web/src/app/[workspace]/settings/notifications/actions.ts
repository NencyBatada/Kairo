"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, type FormState } from "@/lib/forms";

export async function markNotificationRead(brandId: string, id: number) {
  const { response } = await (await coreApi()).POST("/api/brands/{brandId}/notifications/{id}/read", {
    params: { path: { brandId, id } },
  });
  if (response.status === 401) {
    return signInAgain();
  }

  refresh();
}

export async function markAllNotificationsRead(brandId: string) {
  const { response } = await (await coreApi()).POST("/api/brands/{brandId}/notifications/read-all", {
    params: { path: { brandId } },
  });
  if (response.status === 401) {
    return signInAgain();
  }

  refresh();
}

export async function saveEmailSettings(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const kinds = formData
    .getAll("kind")
    .map((value, index) => ({
      kind: String(value),
      email: formData.get(`email-${index}`) === "on",
    }))
    .filter((entry) => entry.kind !== "");

  const { error, response } = await (await coreApi()).PUT("/api/brands/{brandId}/notifications/email-settings", {
    params: { path: { brandId } },
    body: { kinds },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  if (!response.ok) {
    return problemToFormState(error, response.status);
  }

  refresh();
  return { status: "saved", message: "Email settings saved." };
}
