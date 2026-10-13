"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { idle, type FormState } from "@/lib/forms";

type InviteFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function InviteForm({ action }: InviteFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="max-w-md space-y-3">
      <div className="space-y-1">
        <label htmlFor="invite-email" className="block text-sm font-medium">Email</label>
        <input id="invite-email" name="email" type="email" required className="w-full rounded-md border px-3 py-2" />
        <FieldError id="invite-email-error" errors={state.fieldErrors?.email} />
      </div>
      <RoleCheckboxes legend="Roles for the new member" defaultValue={["marketer"]} />
      <FieldError id="invite-roles-error" errors={state.fieldErrors?.roles} />
      <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
        Send invitation
      </button>
      <FormStatus state={state} />
    </form>
  );
}
