"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { Field, FieldDescription } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { idle, type FormState } from "@/lib/forms";
import { DEFAULT_TIME_ZONE } from "@/lib/time-zones";

type CreateWorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  timeZones: string[];
  currencies: string[];
};

export function CreateWorkspaceForm({ action, timeZones, currencies }: CreateWorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <Field>
        <Label htmlFor="name">Brand name</Label>
        <Input id="name" name="name" required maxLength={100} autoComplete="organization" aria-describedby="name-error" aria-invalid={errors.name ? true : undefined} />
        <FieldError id="name-error" errors={errors.name} />
      </Field>
      <Field>
        <Label htmlFor="handle">Handle</Label>
        <FieldDescription id="handle-help">
          3 to 40 lowercase letters, digits and single hyphens, starting with a letter. It appears in your workspace&apos;s web address, like /acme.
          You can&apos;t change it later.
        </FieldDescription>
        <Input
          id="handle"
          name="handle"
          required
          minLength={3}
          maxLength={40}
          autoCapitalize="none"
          spellCheck={false}
          aria-describedby="handle-help handle-error"
          aria-invalid={errors.handle ? true : undefined}
        />
        <FieldError id="handle-error" errors={errors.handle} />
      </Field>
      <div className="grid gap-5 md:grid-cols-2">
        <Field>
          <Label htmlFor="timeZone">Time zone</Label>
          <NativeSelect
            id="timeZone"
            name="timeZone"
            defaultValue={DEFAULT_TIME_ZONE}
            aria-describedby="timeZone-error"
            aria-invalid={errors.timeZone ? true : undefined}
          >
            {timeZones.map((zone) => (
              <option key={zone} value={zone}>
                {zone}
              </option>
            ))}
          </NativeSelect>
          <FieldError id="timeZone-error" errors={errors.timeZone} />
        </Field>
        <Field>
          <Label htmlFor="currency">Currency</Label>
          <NativeSelect
            id="currency"
            name="currency"
            defaultValue="INR"
            aria-describedby="currency-help currency-error"
            aria-invalid={errors.currency ? true : undefined}
          >
            {currencies.map((currency) => (
              <option key={currency} value={currency}>
                {currency}
              </option>
            ))}
          </NativeSelect>
          <FieldDescription id="currency-help">Budgets and reports use it. You can&apos;t change it later.</FieldDescription>
          <FieldError id="currency-error" errors={errors.currency} />
        </Field>
      </div>
      <FormStatus state={state} />
      <Button type="submit" pending={pending} className="self-start max-md:w-full">
        Create workspace
      </Button>
    </form>
  );
}
