"use client";

import Link from "next/link";
import { useActionState, useState } from "react";
import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import type { components } from "@/lib/core-api/schema";
import type { Rule, RuleAction, RuleFormState } from "../_lib/rules";

export function RuleForm({ action, options, festivals, rule, handle }: { action: RuleAction; options: components["schemas"]["TriggerOptions"]; festivals: components["schemas"]["FestivalView"][]; rule?: Rule; handle: string }) {
  const [source, setSource] = useState(rule?.source ?? "weather");
  const [kind, setKind] = useState(rule?.condition.kind ?? "heavy_rain");
  const [dirty, setDirty] = useState(false);
  const [state, submit, pending] = useActionState(async (previous: RuleFormState, form: FormData) => {
    const next = await action(previous, form);
    setDirty(false);
    return next;
  }, { status: "idle" });
  const weather = options.weatherKinds.find((value) => value.kind === kind);
  const regions = options.regions.filter((value) => source === "weather" ? value.kind === "city" : value.kind !== "country");
  const fieldProps = (key: string) => ({ "aria-invalid": !!state.fieldErrors?.[key], "aria-describedby": state.fieldErrors?.[key] ? `${key}-error` : undefined });
  const error = (key: string) => <FieldError id={`${key}-error`} errors={state.fieldErrors?.[key]} />;
  if (state.status === "saved") return <div className="flex flex-col gap-4"><FormStatus state={state} /><Link href={`/${handle}/triggers`} className="text-primary hover:underline">Back to trigger rules</Link></div>;
  return <form action={submit} onReset={(event) => event.preventDefault()} onChange={() => setDirty(true)} className="flex max-w-160 flex-col gap-5">
    <fieldset disabled={pending} className="flex min-w-0 flex-col gap-5" role={state.fieldErrors ? "alert" : undefined}>
      <Field><label htmlFor="rule-name">Rule name</label><Input id="rule-name" name="name" defaultValue={rule?.name} required minLength={2} maxLength={80} {...fieldProps("name")} />{error("name")}</Field>
      <Field><label htmlFor="rule-source">Source</label><NativeSelect id="rule-source" name="source" value={source} onChange={(event) => setSource(event.target.value)} {...fieldProps("source")}><option value="weather">Weather forecast</option><option value="festival">Festival or event</option><option value="stock">Stock level</option></NativeSelect>{error("source")}</Field>
      {source === "weather" && <div className="grid gap-5 md:grid-cols-2">
        <Field><label htmlFor="rule-kind">Weather condition</label><NativeSelect id="rule-kind" name="condition.kind" value={kind} onChange={(event) => setKind(event.target.value)} {...fieldProps("condition.kind")}><option value="heavy_rain">Heavy rain</option><option value="heatwave">Heatwave</option></NativeSelect>{error("condition.kind")}</Field>
        <Field><label htmlFor="rule-threshold">Threshold ({weather?.unit})</label><Input key={kind} id="rule-threshold" name="condition.threshold" type="number" step="0.1" min={Number(weather?.min)} max={Number(weather?.max)} defaultValue={Number(rule?.condition.kind === kind ? rule.condition.threshold : weather?.default)} required {...fieldProps("condition.threshold")} />{error("condition.threshold")}</Field>
        <Field><label htmlFor="rule-within">Forecast within (days)</label><Input id="rule-within" name="condition.withinDays" type="number" min={1} max={Number(options.maxWithinDays)} defaultValue={Number(rule?.condition.withinDays ?? options.defaultWithinDays)} required {...fieldProps("condition.withinDays")} />{error("condition.withinDays")}</Field>
      </div>}
      {source === "festival" && <div className="grid gap-5 md:grid-cols-2">
        <Field><label htmlFor="rule-event">Festival or event</label><NativeSelect id="rule-event" name="condition.eventId" defaultValue={rule?.condition.eventId ?? ""} required {...fieldProps("condition.eventId")}><option value="">Choose an event</option>{festivals.map((festival) => <option key={festival.id} value={festival.id}>{festival.name} · {festival.startsOn}</option>)}</NativeSelect>{error("condition.eventId")}<Link href={`/${handle}/triggers/festivals`} className="text-small text-primary hover:underline">Manage festival calendar</Link></Field>
        <Field><label htmlFor="rule-before">Days before it starts</label><Input id="rule-before" name="condition.daysBefore" type="number" min={0} max={Number(options.maxDaysBefore)} defaultValue={Number(rule?.condition.daysBefore ?? options.defaultDaysBefore)} required {...fieldProps("condition.daysBefore")} />{error("condition.daysBefore")}</Field>
      </div>}
      {source === "stock" && <Field><label htmlFor="rule-units">Minimum units in stock</label><Input id="rule-units" name="condition.minUnits" type="number" min={1} max={100000000} defaultValue={Number(rule?.condition.minUnits ?? 100)} required {...fieldProps("condition.minUnits")} />{error("condition.minUnits")}</Field>}
      <Field><label htmlFor="rule-category">Product category</label><Input id="rule-category" name="category" list="rule-categories" maxLength={750} defaultValue={rule?.category} required {...fieldProps("category")} /><datalist id="rule-categories">{options.categories.map((category) => <option key={category} value={category} />)}</datalist>{error("category")}<p className="text-small text-muted-foreground">Choose a stocked category, or enter the exact category to watch when stock arrives.</p></Field>
      <fieldset className="min-w-0"><legend className="mb-2 text-sm font-medium">{source === "weather" ? "Cities" : "Regions"}</legend><p id="rule-regions-help" className="mb-3 text-small text-muted-foreground">{source === "weather" ? "Choose at least one city for its forecast." : source === "festival" ? "Leave empty to use the event’s markets." : "Leave empty for all of India."} Choose up to {options.maxRegions}.</p><div key={source} className="grid max-h-64 gap-1 overflow-y-auto rounded-md border border-input p-3 sm:grid-cols-2" aria-describedby="rule-regions-help">{regions.map((region) => <label key={region.id} className="flex min-h-8 items-center gap-2 text-small pointer-coarse:min-h-11"><input type="checkbox" name="regions" value={region.id} defaultChecked={rule?.regions.some((value) => value.id === region.id)} className="size-4 accent-primary" {...fieldProps("regions")} /><span>{region.name}</span></label>)}</div>{error("regions")}</fieldset>
      <Field><label htmlFor="rule-cooldown">Cooldown (hours)</label><Input id="rule-cooldown" name="cooldownHours" type="number" min={1} max={Number(options.maxCooldownHours)} defaultValue={Number(rule?.cooldownHours ?? options.defaultCooldownHours)} required {...fieldProps("cooldownHours")} />{error("cooldownHours")}<p className="text-small text-muted-foreground">How long to wait before the rule fires again in the same region.</p></Field>
    </fieldset>
    {state.preview && !dirty && <Alert><h2 className="mb-2 font-semibold">Rule in plain words</h2><p className="wrap-anywhere">{state.preview.description}</p><p className="mt-2">This creates an opportunity to review. Campaigns still need approval.</p></Alert>}
    <FormStatus state={state} />
    <div className="flex flex-col gap-3 sm:flex-row"><Button type="submit" name="intent" value="preview" variant={state.preview && !dirty ? "secondary" : "default"} pending={pending}>Preview rule</Button>{state.preview && !dirty && <Button type="submit" name="intent" value="save" pending={pending}>{rule ? "Save rule" : "Create rule"}</Button>}<Link href={`/${handle}/triggers`} className="inline-flex min-h-9 items-center justify-center text-small text-primary hover:underline pointer-coarse:min-h-11">Cancel</Link></div>
  </form>;
}
