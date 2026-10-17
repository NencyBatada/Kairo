"use client";

import { useActionState, useId, useState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Button, buttonVariants } from "@/components/ui/button";
import { Field, FieldDescription } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { idle, type FormState } from "@/lib/forms";

type CredentialsSheetProps = {
  productName: string;
  scopes: string[];
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

const fields = [
  { name: "clientId", label: "Client ID", type: "text" },
  { name: "clientSecret", label: "Client secret", type: "password" },
  { name: "refreshToken", label: "Refresh token", type: "password" },
] as const;

export function CredentialsSheet({ productName, scopes, action }: CredentialsSheetProps) {
  const [open, setOpen] = useState(false);
  const [state, formAction, pending] = useActionState(async (previous: FormState, formData: FormData) => {
    const result = await action(previous, formData);
    if (result.status === "saved") {
      setOpen(false);
    }
    return result;
  }, idle);
  const id = useId();

  return (
    <Sheet open={open} onOpenChange={(value) => { if (!pending) setOpen(value); }}>
      <SheetTrigger className={buttonVariants({ variant: "secondary", size: "sm" })}>Use client credentials</SheetTrigger>
      <SheetContent side="bottom" className="gap-5 p-5 pb-[max(1.25rem,env(safe-area-inset-bottom))] sm:p-6 sm:pb-[max(1.5rem,env(safe-area-inset-bottom))]">
        <div className="flex flex-col gap-1.5 pr-8">
          <SheetTitle className="wrap-anywhere">Connect {productName} with client credentials</SheetTitle>
          <SheetDescription>
            Use your own Google OAuth client. The refresh token must be issued to this client and include the access below.
          </SheetDescription>
        </div>
        <div className="flex flex-col gap-1.5">
          <p className="text-small font-medium text-foreground">Access {productName} needs</p>
          <ul className="flex flex-col gap-1">
            {scopes.map((scope) => (
              <li key={scope} className="font-mono text-small break-all text-subtle-foreground">{scope}</li>
            ))}
          </ul>
        </div>
        <form action={formAction} className="flex min-w-0 flex-col gap-4">
          {fields.map((field) => {
            const errors = state.fieldErrors?.[field.name];
            return (
              <Field key={field.name}>
                <Label htmlFor={`${id}-${field.name}`}>{field.label}</Label>
                <Input
                  id={`${id}-${field.name}`}
                  name={field.name}
                  type={field.type}
                  required
                  autoComplete="off"
                  spellCheck={false}
                  disabled={pending}
                  aria-describedby={`${id}-${field.name}-error`}
                  aria-invalid={errors ? true : undefined}
                />
                <FieldError id={`${id}-${field.name}-error`} errors={errors} />
              </Field>
            );
          })}
          <FieldDescription>Kairo encrypts these and never shows the secret or token again. Disconnecting deletes Kairo&apos;s copy but doesn&apos;t revoke the token at Google.</FieldDescription>
          <Button type="submit" pending={pending} className="max-sm:w-full self-start">Connect with credentials</Button>
          <FormStatus state={state} />
        </form>
      </SheetContent>
    </Sheet>
  );
}
