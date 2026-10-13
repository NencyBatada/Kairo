"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
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

  return (
    <form action={formAction} className="max-w-md space-y-4">
      <fieldset disabled={!canEdit || pending} className="space-y-4">
        <div className="space-y-1">
          <label htmlFor="name" className="block text-sm font-medium">Brand name</label>
          <input id="name" name="name" defaultValue={workspace.name} required maxLength={100} className="w-full rounded-md border px-3 py-2" />
          <FieldError id="name-error" errors={state.fieldErrors?.name} />
        </div>
        <div className="space-y-1">
          <label htmlFor="timeZone" className="block text-sm font-medium">Time zone</label>
          <select id="timeZone" name="timeZone" defaultValue={workspace.timeZone} className="w-full rounded-md border px-3 py-2">
            {zones.map((zone) => (
              <option key={zone} value={zone}>{zone}</option>
            ))}
          </select>
          <FieldError id="timeZone-error" errors={state.fieldErrors?.timeZone} />
        </div>
      </fieldset>
      <dl className="grid grid-cols-2 gap-2 text-sm">
        <dt className="text-zinc-600 dark:text-zinc-400">Handle</dt>
        <dd>{workspace.handle}</dd>
        <dt className="text-zinc-600 dark:text-zinc-400">Currency</dt>
        <dd>{workspace.currency}</dd>
      </dl>
      {canEdit && (
        <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
          Save changes
        </button>
      )}
      <FormStatus state={state} />
    </form>
  );
}
