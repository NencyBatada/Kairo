import { useId } from "react";

import { FieldError } from "@/components/field-error";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";

import { KitSources } from "./kit-sources";
import type { KitSource } from "./types";

export function TextField({ label, value, onChange, multiline = false, maxLength, help, errors }: {
  label: string; value: string; onChange: (value: string) => void; multiline?: boolean; maxLength?: number; help?: string; errors?: string[];
}) {
  const id = useId();
  const Control = multiline ? Textarea : Input;
  return <div className="flex flex-col gap-1.5">
    <Label htmlFor={id}>{label}</Label>
    {help && <p id={`${id}-help`} className="text-small text-muted-foreground">{help}</p>}
    <Control id={id} value={value} onChange={(e) => onChange(e.target.value)} maxLength={maxLength} aria-invalid={errors?.length ? true : undefined} aria-describedby={[help ? `${id}-help` : "", errors?.length ? `${id}-error` : ""].filter(Boolean).join(" ") || undefined} />
    <FieldError id={`${id}-error`} errors={errors} />
  </div>;
}

type RowField<T> = { key: keyof T; label: string; maxLength?: number; options?: string[] };

export function ObjectRows<T extends object>({ name, singular, rows, fields, empty, onChange, limit, canEdit, errors = {}, path }: {
  name: string; singular: string; rows: T[]; fields: RowField<T>[]; empty: T; onChange: (rows: T[]) => void; limit: number; canEdit: boolean; errors?: Record<string, string[]>; path: string;
}) {
  const id = useId();
  return <fieldset className="flex min-w-0 flex-col gap-3" aria-describedby={errors[path]?.length ? `${id}-error` : undefined}>
    <legend className="mb-3 text-sm font-semibold">{name}</legend>
    {rows.length === 0 && <p className="text-small text-muted-foreground">None added yet.</p>}
    {rows.map((row, index) => <div key={index} className="flex min-w-0 flex-col gap-3 rounded-md border border-border p-3">
      <div className="grid min-w-0 gap-3 md:grid-cols-2">
        {fields.map((field) => <div key={String(field.key)} className="flex min-w-0 flex-col gap-1.5">
          <Label htmlFor={`${id}-${index}-${String(field.key)}`}>{field.label} {index + 1}</Label>
          {field.options ? <NativeSelect id={`${id}-${index}-${String(field.key)}`} aria-invalid={errors[`${path}[${index}].${String(field.key)}`]?.length ? true : undefined} aria-describedby={errors[`${path}[${index}].${String(field.key)}`]?.length ? `${id}-${index}-${String(field.key)}-error` : undefined} value={String(row[field.key] ?? field.options[0])} onChange={(e) => onChange(rows.map((item, i) => i === index ? { ...item, [field.key]: e.target.value } : item))}>
            {field.options.map((option) => <option key={option} value={option}>{option === "never" ? "Never mention" : option === "neutral" ? "Mention factually" : option.charAt(0).toUpperCase() + option.slice(1)}</option>)}
          </NativeSelect> : <Input id={`${id}-${index}-${String(field.key)}`} aria-invalid={errors[`${path}[${index}].${String(field.key)}`]?.length ? true : undefined} aria-describedby={errors[`${path}[${index}].${String(field.key)}`]?.length ? `${id}-${index}-${String(field.key)}-error` : undefined} value={String(row[field.key] ?? "")} maxLength={field.maxLength} onChange={(e) => onChange(rows.map((item, i) => i === index ? { ...item, [field.key]: e.target.value } : item))} />}
          <FieldError id={`${id}-${index}-${String(field.key)}-error`} errors={errors[`${path}[${index}].${String(field.key)}`]} />
        </div>)}
      </div>
      <KitSources sources={"sources" in row ? row.sources as KitSource[] : undefined} />
      {canEdit && <Button variant="destructive" className="self-start" aria-label={`Remove ${singular} ${index + 1}`} onClick={() => onChange(rows.filter((_, i) => i !== index))}>Remove {singular}</Button>}
    </div>)}
    {canEdit && <Button variant="secondary" className="self-start" disabled={rows.length >= limit} onClick={() => onChange([...rows, empty])}>Add {singular}</Button>}
    <FieldError id={`${id}-error`} errors={errors[path]} />
  </fieldset>;
}
