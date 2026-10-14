"use client";

import { useActionState, useId } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import type { ProductAccount } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

export function AccountPicker({ accounts, action, error }: {
  accounts: ProductAccount[];
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  error?: string;
}) {
  const [state, formAction, pending] = useActionState(action, idle);
  const selectId = useId();
  const errorId = useId();

  if (error) {
    return <Alert variant="destructive">{error}</Alert>;
  }
  if (accounts.length === 0) {
    return <Alert variant="warning">No accounts are available to this Google user. Connect again with an account that has access.</Alert>;
  }

  return (
    <form action={formAction} className="flex min-w-0 flex-col items-start gap-3">
      <Field className="w-full max-w-sm">
        <Label htmlFor={selectId}>Account</Label>
        <NativeSelect
          id={selectId}
          name="externalId"
          required
          defaultValue=""
          disabled={pending}
          aria-describedby={errorId}
          aria-invalid={state.fieldErrors?.externalId ? true : undefined}
        >
          <option value="" disabled>Choose an account</option>
          {accounts.map((account) => (
            <option key={account.id} value={account.id}>{account.name} ({account.id})</option>
          ))}
        </NativeSelect>
        <FieldError id={errorId} errors={state.fieldErrors?.externalId} />
      </Field>
      <Button type="submit" size="sm" pending={pending}>Use account</Button>
      <FormStatus state={state} />
    </form>
  );
}
