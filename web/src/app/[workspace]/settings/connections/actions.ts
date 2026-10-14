"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function chooseConnectionAccount(
  brandId: string,
  product: string,
  _state: FormState,
  formData: FormData,
): Promise<FormState> {
  const { error, response } = await (await coreApi()).POST("/api/brands/{brandId}/connections/{product}/account", {
    params: { path: { brandId, product } },
    body: { externalId: text(formData, "externalId") },
  });
  if (response.status === 401) {
    return signInAgain();
  }
  if (!response.ok) {
    return problemToFormState(error, response.status);
  }
  refresh();
  return { status: "saved", message: "Account connected." };
}

export async function checkConnection(
  brandId: string,
  product: string,
  _state: FormState,
  _formData: FormData,
): Promise<FormState> {
  void _state;
  void _formData;
  const { data, error, response } = await (await coreApi()).POST("/api/brands/{brandId}/connections/{product}/check", {
    params: { path: { brandId, product } },
  });
  if (response.status === 401) {
    return signInAgain();
  }
  if (!response.ok) {
    return problemToFormState(error, response.status);
  }
  refresh();
  return data?.problem
    ? { status: "error", message: data.problem }
    : { status: "saved", message: "Access confirmed." };
}

export async function disconnectConnection(
  brandId: string,
  product: string,
  _state: FormState,
  _formData: FormData,
): Promise<FormState> {
  void _state;
  void _formData;
  const { error, response } = await (await coreApi()).DELETE("/api/brands/{brandId}/connections/{product}", {
    params: { path: { brandId, product } },
  });
  if (response.status === 401) {
    return signInAgain();
  }
  if (!response.ok) {
    return problemToFormState(error, response.status);
  }
  refresh();
  return { status: "saved", message: "Connection removed." };
}
