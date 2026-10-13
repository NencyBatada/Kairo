"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function updateWorkspace(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).PATCH("/api/brands/{brandId}", {
    params: { path: { brandId } },
    body: { name: text(formData, "name"), timeZone: text(formData, "timeZone") },
  });
  return finish(error, response, "Saved.");
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
