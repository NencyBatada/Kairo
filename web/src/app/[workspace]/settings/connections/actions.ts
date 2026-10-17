"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

function connectionUnavailable(): FormState {
  return { status: "error", message: "Could not reach the service. Please try again." };
}

export async function chooseConnectionAccount(
  brandId: string,
  product: string,
  _state: FormState,
  formData: FormData,
): Promise<FormState> {
  const result = await (await coreApi()).POST("/api/brands/{brandId}/connections/{product}/account", {
    params: { path: { brandId, product } },
    body: { externalId: text(formData, "externalId") },
  }).catch(() => null);
  if (!result) {
    return connectionUnavailable();
  }
  const { error, response } = result;
  if (response.status === 401) {
    return signInAgain();
  }
  if (!response.ok) {
    return problemToFormState(error, response.status);
  }
  refresh();
  return { status: "saved", message: "Account connected." };
}

export async function connectWithCredentials(
  brandId: string,
  product: string,
  _state: FormState,
  formData: FormData,
): Promise<FormState> {
  const result = await (await coreApi()).POST("/api/brands/{brandId}/connections/{product}/credentials", {
    params: { path: { brandId, product } },
    body: {
      clientId: text(formData, "clientId"),
      clientSecret: text(formData, "clientSecret"),
      refreshToken: text(formData, "refreshToken"),
    },
  }).catch(() => null);
  if (!result) {
    return connectionUnavailable();
  }
  const { error, response } = result;
  if (response.status === 401) {
    return signInAgain();
  }
  if (!response.ok) {
    return problemToFormState(error, response.status);
  }
  refresh();
  return { status: "saved", message: "Credentials accepted. Choose the account to use." };
}

export async function checkConnection(
  brandId: string,
  product: string,
  _state: FormState,
  _formData: FormData,
): Promise<FormState> {
  void _state;
  void _formData;
  const result = await (await coreApi()).POST("/api/brands/{brandId}/connections/{product}/check", {
    params: { path: { brandId, product } },
  }).catch(() => null);
  if (!result) {
    return connectionUnavailable();
  }
  const { data, error, response } = result;
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
  const result = await (await coreApi()).DELETE("/api/brands/{brandId}/connections/{product}", {
    params: { path: { brandId, product } },
  }).catch(() => null);
  if (!result) {
    return connectionUnavailable();
  }
  const { error, response } = result;
  if (response.status === 401) {
    return signInAgain();
  }
  if (!response.ok) {
    return problemToFormState(error, response.status);
  }
  refresh();
  return { status: "saved", message: "Connection removed." };
}
