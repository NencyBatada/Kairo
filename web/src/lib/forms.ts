export type FormState = {
  status: "idle" | "saved" | "error";
  message?: string;
  fieldErrors?: Record<string, string[]>;
};

export const idle: FormState = { status: "idle" };

type Problem = {
  title?: string | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
};

export function problemToFormState(problem: unknown, status: number): FormState {
  const details = (problem ?? {}) as Problem;
  if (details.errors && Object.keys(details.errors).length > 0) {
    return { status: "error", fieldErrors: details.errors };
  }

  return { status: "error", message: details.detail ?? details.title ?? fallbackMessage(status) };
}

export function text(formData: FormData, name: string) {
  const value = formData.get(name);
  return typeof value === "string" ? value.trim() : "";
}

function fallbackMessage(status: number) {
  if (status === 403) {
    return "You don't have permission to do that.";
  }

  if (status === 404) {
    return "That no longer exists.";
  }

  return "Something went wrong. Please try again.";
}
