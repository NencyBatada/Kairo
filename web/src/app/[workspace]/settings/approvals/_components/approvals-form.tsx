"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import type { ApprovalSettings } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type Duty = "briefApprovers" | "assetApprovers" | "launchApprovers" | "budgetOwners";

const duties: { field: Duty; label: string; help: string }[] = [
  { field: "briefApprovers", label: "Brief approvers", help: "Approve briefs, gate 1." },
  { field: "assetApprovers", label: "Asset approvers", help: "Approve the asset set, gate 2." },
  { field: "launchApprovers", label: "Launch approvers", help: "Approve launches, gate 3." },
  { field: "budgetOwners", label: "Budget owners", help: "Approve briefs and launches above the budget limit." },
];

type ApprovalsFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  settings: ApprovalSettings;
  approvers: { userId: string; name: string }[];
  canEdit: boolean;
};

export function ApprovalsForm({ action, settings, approvers, canEdit }: ApprovalsFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="max-w-xl space-y-6">
      <fieldset disabled={!canEdit || pending} className="space-y-6">
        {duties.map((duty) => (
          <fieldset key={duty.field} className="space-y-2">
            <legend className="font-medium">{duty.label}</legend>
            <p className="text-sm text-zinc-600 dark:text-zinc-400">{duty.help}</p>
            {approvers.map((approver) => (
              <label key={approver.userId} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  name={duty.field}
                  value={approver.userId}
                  defaultChecked={settings[duty.field].includes(approver.userId)}
                />
                {approver.name}
              </label>
            ))}
            <FieldError id={`${duty.field}-error`} errors={state.fieldErrors?.[duty.field]} />
          </fieldset>
        ))}
        <div className="space-y-1">
          <label htmlFor="budgetLimit" className="block font-medium">
            Per-campaign budget limit ({settings.currency})
          </label>
          <input
            id="budgetLimit"
            name="budgetLimit"
            type="number"
            min="0"
            step="0.01"
            defaultValue={settings.budgetLimit ?? ""}
            aria-describedby="budgetLimit-help"
            className="w-full rounded-md border px-3 py-2"
          />
          <p id="budgetLimit-help" className="text-sm text-zinc-600 dark:text-zinc-400">
            Leave it empty and every brief and launch goes to a budget owner.
          </p>
          <FieldError id="budgetLimit-error" errors={state.fieldErrors?.budgetLimit} />
        </div>
      </fieldset>
      {canEdit && (
        <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
          Save approval settings
        </button>
      )}
      <FormStatus state={state} />
    </form>
  );
}
