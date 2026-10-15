import Link from "next/link";

import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";

import { type CampaignFilters as Filters, type FilterErrors, stages, statuses, validDay } from "../_lib/filters";

export function CampaignFilters({ values, errors, owners, base, timeZone }: { values: Filters; errors: FilterErrors; owners: { id: string; name: string }[]; base: string; timeZone: string }) {
  const fields = (name: keyof Filters) => ({ id: `campaign-${name}`, name, "aria-invalid": !!errors[name] || undefined, "aria-describedby": errors[name] ? `campaign-${name}-error` : undefined });
  const error = (name: keyof Filters) => errors[name] ? <p id={`campaign-${name}-error`} className="text-small text-destructive-strong">{errors[name]}</p> : null;
  return <form key={JSON.stringify(values)} action={base} method="get" aria-label="Filter campaigns" className="grid gap-4 rounded-lg border border-border bg-card p-4 md:grid-cols-2 md:p-5 xl:grid-cols-3">
    <Field><label htmlFor="campaign-q">Search campaigns or products</label><Input {...fields("q")} type="search" defaultValue={values.q} maxLength={100} placeholder="Name or product title" />{error("q")}</Field>
    <Field><label htmlFor="campaign-stage">Stage</label><NativeSelect {...fields("stage")} defaultValue={Object.hasOwn(stages, values.stage) ? values.stage : ""}><option value="">All stages</option>{Object.entries(stages).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</NativeSelect>{error("stage")}</Field>
    <Field><label htmlFor="campaign-status">Status</label><NativeSelect {...fields("status")} defaultValue={Object.hasOwn(statuses, values.status) ? values.status : ""}><option value="">All statuses</option>{Object.entries(statuses).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</NativeSelect>{error("status")}</Field>
    <Field><label htmlFor="campaign-owner">Owner</label><NativeSelect {...fields("owner")} defaultValue={values.owner}><option value="">All owners</option>{owners.map((owner) => <option key={owner.id} value={owner.id}>{owner.name}</option>)}</NativeSelect>{error("owner")}</Field>
    <Field><label htmlFor="campaign-from">Opened from</label><Input {...fields("from")} type="date" defaultValue={validDay(values.from) ? values.from : ""} />{error("from")}</Field>
    <Field><label htmlFor="campaign-to">Opened through</label><Input {...fields("to")} type="date" defaultValue={validDay(values.to) ? values.to : ""} />{error("to")}</Field>
    <p className="text-small text-muted-foreground md:col-span-2 xl:col-span-3">Dates include the whole day in the workspace time zone: {timeZone}.</p>
    <div className="flex flex-wrap items-center gap-3 md:col-span-2 xl:col-span-3"><Button type="submit" className="w-full sm:w-auto">Apply filters</Button>{Object.values(values).some(Boolean) && <Link href={base} className="inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11">Clear filters</Link>}</div>
  </form>;
}
