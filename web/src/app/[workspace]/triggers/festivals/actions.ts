"use server";

import { refresh, revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, type FormState } from "@/lib/forms";
import { readFestival } from "./_lib/calendar";

export async function saveFestival(brandId: string, festivalId: string | null, _previous: FormState, form: FormData): Promise<FormState> {
  void _previous;
  const { request, errors } = readFestival(form);
  if (Object.keys(errors).length) return { status: "error", fieldErrors: errors };
  let result;
  try {
    const api = await coreApi();
    result = festivalId ? await api.PUT("/api/brands/{brandId}/triggers/festivals/{festivalId}", { params: { path: { brandId, festivalId } }, body: request }) : await api.POST("/api/brands/{brandId}/triggers/festivals", { params: { path: { brandId } }, body: request });
  } catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  refresh();
  return { status: "saved", message: festivalId ? "Event updated." : "Event added to your calendar." };
}

export async function removeFestival(brandId: string, festivalId: string, handle: string, _previous: FormState, _form: FormData): Promise<FormState> {
  void _previous; void _form;
  let result;
  try { result = await (await coreApi()).DELETE("/api/brands/{brandId}/triggers/festivals/{festivalId}", { params: { path: { brandId, festivalId } } }); }
  catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  revalidatePath(`/${encodeURIComponent(handle)}/triggers/festivals`);
  redirect(`/${encodeURIComponent(handle)}/triggers/festivals`);
}
