"use client";

import { useActionState, useState } from "react";

import { FieldError } from "@/components/field-error";
import { useAskEditGuard } from "@/components/ask-kairo/edit-guard";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { idle } from "@/lib/forms";

import type { CatalogueAction, CatalogueActionState, FixField, ProductDetail } from "../_lib/types";

const fields: { key: FixField; label: string; maxLength?: number; type?: string }[] = [
  { key: "title", label: "Title", maxLength: 150 }, { key: "price", label: "Price", type: "number" },
  { key: "link", label: "Product link", type: "url" }, { key: "imageLink", label: "Image link", type: "url" }, { key: "category", label: "Category" },
];
const valuesOf = (product: ProductDetail) => Object.fromEntries([...fields.map(({ key }) => [key, String(product[key] ?? "")]), ["currency", product.currency ?? ""]]);

export function ProductFixes({ product, canFix, action }: { product: ProductDetail; canFix: boolean; action: CatalogueAction }) {
  const [values, setValues] = useState(valuesOf(product));
  const [clear, setClear] = useState<Record<string, boolean>>({});
  const [dirty, setDirty] = useState(false);
  const token = JSON.stringify(product);
  const [seen, setSeen] = useState(token);
  const [stale, setStale] = useState(false);
  const [state, submit, pending] = useActionState(async (previous: CatalogueActionState, data: FormData) => {
    const result = await action(previous, data);
    if (result.status === "saved") { setDirty(false); setClear({}); }
    return result;
  }, idle);
  useAskEditGuard(dirty || pending);
  if (token !== seen) {
    setSeen(token);
    if (dirty) setStale(true);
    else { setValues(valuesOf(product)); setClear({}); setStale(false); }
  }
  const errors = state.fieldErrors ?? {};
  const editable = (field: FixField) => product.fixed.includes(field) || (field === "imageLink" && product.fixed.includes("image")) || product[field] === null || product[field] === "";
  return <Card><CardHeader><CardTitle>Missing details and fixes</CardTitle></CardHeader><CardContent className="flex flex-col gap-4">
    <p className="text-small text-muted-foreground">Fixes fill gaps while the source leaves a field empty. When the file or feed supplies a value, its value takes over.</p>
    <Alert variant={product.missing.includes("stock") ? "warning" : "neutral"}>Correct stock in the file or feed, then import or refresh it. Stock cannot be overridden here.</Alert>
    {!canFix && <Alert>Only admins and marketers can fix product details.</Alert>}
    {stale && <Alert variant="warning"><p>This product changed in the source. Your edits have been kept. Copy anything you need before loading the latest details.</p><Button variant="secondary" className="mt-2 self-start" onClick={() => { setValues(valuesOf(product)); setClear({}); setDirty(false); setStale(false); }}>Load latest details</Button></Alert>}
    <form action={submit} className="flex max-w-160 flex-col gap-5">
      {fields.map(({ key, label, type = "text", maxLength }) => <div key={key} className="flex flex-col gap-1.5">
        <Label htmlFor={`fix-${key}`}>{label}</Label>
        <Input id={`fix-${key}`} name={key} type={type} min={type === "number" ? "0" : undefined} step={type === "number" ? "0.01" : undefined} maxLength={maxLength} value={values[key]} onChange={(e) => { setValues({ ...values, [key]: e.target.value }); setDirty(true); }} disabled={!canFix || !editable(key) || clear[key] || pending || stale} aria-invalid={errors[key]?.length ? true : undefined} aria-describedby={`fix-${key}-error fix-${key}-help`} />
        <p id={`fix-${key}-help`} className="text-small text-muted-foreground">{editable(key) ? product.fixed.includes(key) ? "A fix currently fills this field." : "Missing from the source." : "Supplied by the source. Change it in the file or feed."}</p>
        <FieldError id={`fix-${key}-error`} errors={errors[key]} />
        {canFix && product.fixed.includes(key) && <label className="flex min-h-6 items-center gap-2 text-small pointer-coarse:min-h-11"><Checkbox name={`clear:${key}`} checked={!!clear[key]} disabled={pending || stale} onChange={(e) => { setClear({ ...clear, [key]: e.target.checked }); setDirty(true); }} />Clear {label.toLowerCase()} fix</label>}
      </div>)}
      <div className="flex flex-col gap-1.5"><Label htmlFor="fix-currency">Price currency</Label><Input id="fix-currency" name="currency" value={values.currency} maxLength={3} onChange={(e) => { setValues({ ...values, currency: e.target.value }); setDirty(true); }} disabled={!canFix || !editable("price") || clear.price || pending || stale} aria-invalid={errors.currency?.length ? true : undefined} aria-describedby="fix-currency-error" /><FieldError id="fix-currency-error" errors={errors.currency} /></div>
      <FormStatus state={state} />
      {Object.keys(errors).length > 0 && <Alert variant="destructive">Review the marked fields and save again.</Alert>}
      {canFix && <Button type="submit" pending={pending} disabled={stale} className="self-start max-sm:w-full">Save missing details</Button>}
    </form>
  </CardContent></Card>;
}
