"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function addTopic(brandId: string, _previous: FormState, form: FormData): Promise<FormState> {
  void _previous;
  const phrase = text(form, "phrase").split(/\s+/).join(" ");
  const kind = text(form, "kind");
  const fieldErrors: Record<string, string[]> = {};
  if (phrase.length < 2 || phrase.length > 100) fieldErrors.phrase = ["Use 2 to 100 characters."];
  if (!["watch", "ignore"].includes(kind)) fieldErrors.kind = ["Choose Watch or Ignore."];
  if (Object.keys(fieldErrors).length) return { status: "error", fieldErrors };
  let result;
  try { result = await (await coreApi()).POST("/api/brands/{brandId}/opportunities/topics", { params: { path: { brandId } }, body: { phrase, kind } }); }
  catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  refresh();
  return { status: "saved", message: kind === "watch" ? "Topic added to Watched." : "Topic added to Ignored." };
}

export async function removeTopic(brandId: string, topicId: string, _previous: FormState, _form: FormData): Promise<FormState> {
  void _previous; void _form;
  let result;
  try { result = await (await coreApi()).DELETE("/api/brands/{brandId}/opportunities/topics/{topicId}", { params: { path: { brandId, topicId } } }); }
  catch { return { status: "error", message: "Kairo couldn't reach the service. Please try again." }; }
  if (result.response.status === 401) return signInAgain();
  if (!result.response.ok) return problemToFormState(result.error, result.response.status);
  refresh();
  return { status: "saved", message: "Topic removed." };
}
