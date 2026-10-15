"use client";

import Link from "next/link";
import { useActionState } from "react";
import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import type { components } from "@/lib/core-api/schema";
import { idle, type FormState } from "@/lib/forms";
import type { Festival } from "../_lib/calendar";

export function FestivalForm({ event, regions, handle, action }: { event?: Festival; regions: components["schemas"]["PlaceView"][]; handle: string; action: (previous: FormState, form: FormData) => Promise<FormState> }) {
  const [state, submit, pending] = useActionState(action, idle);
  const props = (key: string) => ({ "aria-invalid": !!state.fieldErrors?.[key], "aria-describedby": state.fieldErrors?.[key] ? `event-${key}-error` : undefined });
  const error = (key: string) => <FieldError id={`event-${key}-error`} errors={state.fieldErrors?.[key]} />;
  if (state.status === "saved") return <div className="flex flex-col gap-4"><FormStatus state={state} /><Link href={`/${handle}/triggers/festivals`} className="text-primary hover:underline">Back to festival calendar</Link></div>;
  return <form action={submit} onReset={(e) => e.preventDefault()} className="flex max-w-160 flex-col gap-5">
    {event && Number(event.rules) > 0 && <Alert variant="warning">{event.rules} trigger {Number(event.rules) === 1 ? "rule uses" : "rules use"} this event. Changes to its dates and markets affect future checks.</Alert>}
    <fieldset disabled={pending} role={state.fieldErrors ? "alert" : undefined} className="flex min-w-0 flex-col gap-5">
      <Field><label htmlFor="event-name">Event name</label><Input id="event-name" name="name" defaultValue={event?.name} required minLength={2} maxLength={80} {...props("name")} />{error("name")}</Field>
      <div className="grid gap-5 md:grid-cols-2"><Field><label htmlFor="event-start">Starts on</label><Input id="event-start" name="startsOn" type="date" defaultValue={event?.startsOn} required {...props("startsOn")} />{error("startsOn")}</Field><Field><label htmlFor="event-end">Ends on</label><Input id="event-end" name="endsOn" type="date" defaultValue={event?.endsOn} {...props("endsOn")} />{error("endsOn")}<p className="text-small text-muted-foreground">Leave empty for one day. Events can last up to 14 days.</p></Field></div>
      <fieldset className="min-w-0"><legend className="mb-2 text-sm font-medium">Markets</legend><p className="mb-3 text-small text-muted-foreground">Choose states, or leave empty for all of India.</p><div className="grid max-h-72 gap-1 overflow-y-auto rounded-md border border-input p-3 sm:grid-cols-2">{regions.filter((region) => region.kind === "state").map((region) => <label key={region.id} className="flex min-h-8 items-center gap-2 text-small pointer-coarse:min-h-11"><input type="checkbox" name="regions" value={region.id} defaultChecked={event?.regions.some((value) => value.id === region.id)} className="size-4 accent-primary" {...props("regions")} />{region.name}</label>)}</div>{error("regions")}</fieldset>
    </fieldset>
    <FormStatus state={state} /><div className="flex flex-col gap-3 sm:flex-row"><Button type="submit" pending={pending}>{event ? "Save event" : "Add event"}</Button><Link href={`/${handle}/triggers/festivals`} className="inline-flex min-h-9 items-center justify-center text-small text-primary hover:underline pointer-coarse:min-h-11">Cancel</Link></div>
  </form>;
}
