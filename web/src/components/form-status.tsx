import { CircleAlert, CircleCheck } from "lucide-react";

import type { FormState } from "@/lib/forms";
import { cn } from "@/lib/utils";

export function FormStatus({ state }: { state: FormState }) {
  const message = state.message || (state.status === "error" && Object.values(state.fieldErrors ?? {}).some(errors => errors.length > 0) ? "Check the highlighted fields and try again." : undefined);
  if (!message) {
    return null;
  }

  const failed = state.status === "error";
  const Icon = failed ? CircleAlert : CircleCheck;

  return (
    <p
      role={failed ? "alert" : "status"}
      className={cn("flex items-start gap-2 text-small", failed ? "text-destructive-strong" : "text-success-strong")}
    >
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span>{message}</span>
    </p>
  );
}
