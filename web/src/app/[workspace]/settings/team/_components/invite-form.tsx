"use client";

import { UserPlus } from "lucide-react";
import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { idle, type FormState } from "@/lib/forms";

type InviteFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function InviteForm({ action }: InviteFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <Field>
        <Label htmlFor="invite-email">Email</Label>
        <Input
          id="invite-email"
          name="email"
          type="email"
          required
          autoComplete="off"
          placeholder="name@company.com"
          aria-describedby="invite-email-error"
          aria-invalid={errors.email ? true : undefined}
        />
        <FieldError id="invite-email-error" errors={errors.email} />
      </Field>
      <Field>
        <p aria-hidden="true" className="text-small font-semibold text-foreground">
          Roles
        </p>
        <RoleCheckboxes legend="Roles for the new member" defaultValue={["marketer"]} />
        <FieldError id="invite-roles-error" errors={errors.roles} />
      </Field>
      <div className="flex flex-col gap-3">
        <Button type="submit" pending={pending} className="self-start max-md:w-full">
          <UserPlus aria-hidden="true" />
          Send invitation
        </Button>
        <FormStatus state={state} />
      </div>
    </form>
  );
}
