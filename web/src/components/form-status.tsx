import type { FormState } from "@/lib/forms";

export function FormStatus({ state }: { state: FormState }) {
  if (!state.message) {
    return null;
  }

  return state.status === "error" ? (
    <p role="alert" className="text-sm text-red-700 dark:text-red-300">
      {state.message}
    </p>
  ) : (
    <p role="status" className="text-sm text-green-700 dark:text-green-300">
      {state.message}
    </p>
  );
}
