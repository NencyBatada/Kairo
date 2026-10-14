"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import type { EmailSettings } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type EmailSettingsFormProps = {
  brandId: string;
  settings: EmailSettings;
  action: (brandId: string, state: FormState, formData: FormData) => Promise<FormState>;
};

export function EmailSettingsForm({ brandId, settings, action }: EmailSettingsFormProps) {
  const [state, formAction, pending] = useActionState(action.bind(null, brandId), idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-4">
      <Card>
        <CardContent className="flex flex-col gap-4 pt-0">
          <p className="text-small text-muted-foreground">
            Choose which events also send you an email. Approval requests and launch problems always email you when you receive them.
          </p>
          <ul className="flex flex-col gap-4">
            {settings.kinds.map((kind, index) => (
              <li key={kind.kind} className="flex flex-col gap-1.5 border-b border-border pb-4 last:border-b-0 last:pb-0">
                <input type="hidden" name="kind" value={kind.kind} />
                {!kind.canTurnOff && <input type="hidden" name={`email-${index}`} value="on" />}
                <label className="flex min-h-6 cursor-pointer items-start gap-2.5 text-sm text-foreground pointer-coarse:min-h-11">
                  <Checkbox
                    name={kind.canTurnOff ? `email-${index}` : undefined}
                    defaultChecked={kind.email}
                    disabled={!kind.canTurnOff || pending}
                    className="mt-0.5"
                  />
                  <span className="min-w-0 flex-1">
                    <span className="block font-semibold">{kind.name}</span>
                    <span className="mt-0.5 block text-small text-subtle-foreground">{kind.description}</span>
                    {!kind.canTurnOff && (
                      <span className="mt-1 block text-xs text-subtle-foreground">This email cannot be turned off.</span>
                    )}
                  </span>
                </label>
              </li>
            ))}
          </ul>
          <FieldError id="email-settings-error" errors={errors.kinds} />
        </CardContent>
      </Card>
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" disabled={pending}>
          Save email settings
        </Button>
        <FormStatus state={state} />
      </div>
    </form>
  );
}
