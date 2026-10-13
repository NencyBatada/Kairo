"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { idle, type FormState } from "@/lib/forms";
import { DEFAULT_TIME_ZONE } from "@/lib/time-zones";

type CreateWorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  timeZones: string[];
  currencies: string[];
};

export function CreateWorkspaceForm({ action, timeZones, currencies }: CreateWorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="space-y-4">
      <div className="space-y-1">
        <label htmlFor="name" className="block text-sm font-medium">Brand name</label>
        <input id="name" name="name" required maxLength={100} aria-describedby="name-error" className="w-full rounded-md border px-3 py-2" />
        <FieldError id="name-error" errors={state.fieldErrors?.name} />
      </div>
      <div className="space-y-1">
        <label htmlFor="handle" className="block text-sm font-medium">Handle</label>
        <input id="handle" name="handle" required minLength={3} maxLength={40} aria-describedby="handle-error" className="w-full rounded-md border px-3 py-2" />
        <FieldError id="handle-error" errors={state.fieldErrors?.handle} />
      </div>
      <div className="space-y-1">
        <label htmlFor="timeZone" className="block text-sm font-medium">Time zone</label>
        <select id="timeZone" name="timeZone" defaultValue={DEFAULT_TIME_ZONE} className="w-full rounded-md border px-3 py-2">
          {timeZones.map((zone) => (
            <option key={zone} value={zone}>{zone}</option>
          ))}
        </select>
        <FieldError id="timeZone-error" errors={state.fieldErrors?.timeZone} />
      </div>
      <div className="space-y-1">
        <label htmlFor="currency" className="block text-sm font-medium">Currency</label>
        <select id="currency" name="currency" defaultValue="INR" className="w-full rounded-md border px-3 py-2">
          {currencies.map((currency) => (
            <option key={currency} value={currency}>{currency}</option>
          ))}
        </select>
        <FieldError id="currency-error" errors={state.fieldErrors?.currency} />
      </div>
      {state.message && <p role="alert" className="text-sm text-red-700 dark:text-red-300">{state.message}</p>}
      <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
        Create workspace
      </button>
    </form>
  );
}
