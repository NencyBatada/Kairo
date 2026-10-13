"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import type { WorkspaceDetails } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type WorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  workspace: WorkspaceDetails;
  timeZones: string[];
  canEdit: boolean;
};

export function WorkspaceForm({ action, workspace, timeZones, canEdit }: WorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const zones = timeZones.includes(workspace.timeZone) ? timeZones : [workspace.timeZone, ...timeZones];
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="max-w-160">
      <Card>
        <CardHeader>
          <CardTitle>Workspace details</CardTitle>
          <CardDescription>{canEdit ? "The name and time zone everyone in this workspace sees." : "Only admins can change these details."}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <fieldset disabled={!canEdit || pending} className="flex min-w-0 flex-col gap-5">
            <Field>
              <Label htmlFor="name">Brand name</Label>
              <Input
                id="name"
                name="name"
                defaultValue={workspace.name}
                required
                maxLength={100}
                aria-describedby="name-error"
                aria-invalid={errors.name ? true : undefined}
              />
              <FieldError id="name-error" errors={errors.name} />
            </Field>
            <Field>
              <Label htmlFor="timeZone">Time zone</Label>
              <NativeSelect id="timeZone" name="timeZone" defaultValue={workspace.timeZone} aria-describedby="timeZone-error">
                {zones.map((zone) => (
                  <option key={zone} value={zone}>
                    {zone}
                  </option>
                ))}
              </NativeSelect>
              <FieldError id="timeZone-error" errors={errors.timeZone} />
            </Field>
          </fieldset>
          <div className="flex flex-col gap-2 rounded-md bg-muted px-4 py-3">
            <dl className="grid grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-8 gap-y-2 text-sm">
              <dt className="text-subtle-foreground">Handle</dt>
              <dd className="font-mono text-small break-all text-foreground">{workspace.handle}</dd>
              <dt className="text-subtle-foreground">Currency</dt>
              <dd className="text-foreground">{workspace.currency}</dd>
            </dl>
            <p className="text-small text-subtle-foreground">The handle and currency can&apos;t be changed after a workspace is created.</p>
          </div>
        </CardContent>
        {canEdit && (
          <CardFooter>
            <Button type="submit" pending={pending}>
              Save changes
            </Button>
            <FormStatus state={state} />
          </CardFooter>
        )}
      </Card>
    </form>
  );
}
