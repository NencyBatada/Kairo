"use client";

import { useId, useState } from "react";
import { FieldError } from "@/components/field-error";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { NativeSelect } from "@/components/ui/native-select";
import { Button } from "@/components/ui/button";
import { adLength, callsToAction } from "../_lib/edit";
import type { Asset } from "../_lib/model";

export function AssetFields({ asset, errors }: { asset: Asset; errors?: Record<string, string[]> }) {
  const content = asset.content;
  return <div className="grid min-w-0 gap-5" lang={asset.language === "zxx" ? undefined : asset.language}>
    {asset.kind === "keyword_set" && <KeywordFields asset={asset} error={errors?.keywords} />}
    {["search_ad", "demand_gen_text"].includes(asset.kind) && <>
      <TextField name="headlines" label="Headlines" value={content.headlines?.join("\n") ?? ""} help={asset.kind === "search_ad" ? "3 to 15 lines · 30 characters per line" : "1 to 5 lines · 40 characters per line; at least one within 30"} error={errors?.headlines} lines />
      {asset.kind === "demand_gen_text" && <TextField name="longHeadlines" label="Long headlines" value={content.longHeadlines?.join("\n") ?? ""} help="1 to 5 lines · 90 characters per line" error={errors?.longHeadlines} lines />}
      <TextField name="descriptions" label="Descriptions" value={content.descriptions?.join("\n") ?? ""} help={asset.kind === "search_ad" ? "2 to 4 lines · 90 characters per line" : "1 to 5 lines · 90 characters per line"} error={errors?.descriptions} lines />
    </>}
    {asset.kind === "search_ad" && <div className="grid min-w-0 gap-5 md:grid-cols-2"><TextField name="path1" label="First display path (optional)" value={content.path1 ?? ""} help="15 characters, no spaces" error={errors?.path1} /><TextField name="path2" label="Second display path (optional)" value={content.path2 ?? ""} help="15 characters; requires the first path" error={errors?.path2} /></div>}
    {asset.kind === "demand_gen_text" && <><TextField name="businessName" label="Business name" value={content.businessName ?? ""} help="Up to 25 characters" error={errors?.businessName} /><ActionField value={content.callToAction ?? ""} error={errors?.callToAction} /></>}
    {asset.kind === "shopping_title" && <TextField name="title" label="Campaign shopping title" value={content.title ?? ""} help="Up to 150 characters. The catalogue title stays unchanged." error={errors?.title} lines />}
  </div>;
}

function TextField({ name, label, value, help, error, lines = false }: { name: string; label: string; value: string; help: string; error?: string[]; lines?: boolean }) {
  const id = useId(); const [text, setText] = useState(value);
  const shared = { id, name, value: text, onChange: (event: { target: { value: string } }) => setText(event.target.value), "aria-describedby": `${id}-help${error ? ` ${id}-error` : ""}`, "aria-invalid": !!error };
  return <div className="grid min-w-0 gap-1.5"><label htmlFor={id} className="text-sm font-medium">{label}</label><p id={`${id}-help`} className="text-xs text-subtle-foreground">{help}{lines && name !== "title" ? ". One item per line." : ""}</p>{lines ? <Textarea {...shared} rows={Math.min(8, Math.max(3, text.split("\n").length))} /> : <Input {...shared} />}<p className="text-xs wrap-anywhere text-subtle-foreground">{lines && name !== "title" ? `Characters per line: ${text.split("\n").map(adLength).join(", ")}` : `${adLength(text)} characters`}</p><FieldError id={`${id}-error`} errors={error} /></div>;
}

function ActionField({ value, error }: { value: string; error?: string[] }) {
  const id = useId();
  return <div className="grid gap-1.5"><label htmlFor={id} className="text-sm font-medium">Call to action</label><NativeSelect id={id} name="callToAction" defaultValue={value} aria-invalid={!!error} aria-describedby={error ? `${id}-error` : undefined}>{!callsToAction.includes(value) && <option value="">Choose a call to action</option>}{callsToAction.map((action) => <option key={action} value={action}>{action.replaceAll("_", " ")}</option>)}</NativeSelect><FieldError id={`${id}-error`} errors={error} /></div>;
}

function KeywordFields({ asset, error }: { asset: Asset; error?: string[] }) {
  const id = useId();
  const [rows, setRows] = useState((asset.content.keywords ?? []).map((keyword, i) => ({ ...keyword, key: i })));
  const [next, setNext] = useState(rows.length);
  return <fieldset className="grid min-w-0 gap-3" aria-describedby={`${id}-help${error ? ` ${id}-error` : ""}`}><legend className="mb-2 text-sm font-medium">Keywords</legend><p id={`${id}-help`} className="text-xs text-subtle-foreground">1 to 50 keywords · Up to 80 characters and 10 words each</p>{rows.map((row, i) => <div key={row.key} className="grid min-w-0 gap-2 rounded-md border border-border p-3"><label htmlFor={`${id}-${row.key}`} className="text-sm">Keyword {i + 1}</label><Input id={`${id}-${row.key}`} name="keywordText" value={row.text} aria-invalid={!!error} aria-describedby={error ? `${id}-error` : `${id}-help`} onChange={(event) => setRows(rows.map((item) => item.key === row.key ? { ...item, text: event.target.value } : item))} /><label htmlFor={`${id}-match-${row.key}`} className="text-xs text-subtle-foreground">Match type for keyword {i + 1}</label><NativeSelect id={`${id}-match-${row.key}`} name="keywordMatch" value={row.match} onChange={(event) => setRows(rows.map((item) => item.key === row.key ? { ...item, match: event.target.value } : item))}>{["broad", "phrase", "exact"].map((match) => <option key={match} value={match}>{match}</option>)}</NativeSelect><div className="flex flex-wrap items-center justify-between gap-2"><span className="text-xs text-subtle-foreground">{adLength(row.text)} characters</span><Button variant="ghost" onClick={() => setRows(rows.filter((item) => item.key !== row.key))} aria-label={`Remove keyword ${i + 1}`}>Remove</Button></div></div>)}<Button className="w-fit" variant="secondary" disabled={rows.length >= 50} onClick={() => { setRows([...rows, { key: next, text: "", match: "broad" }]); setNext(next + 1); }}>Add keyword</Button><FieldError id={`${id}-error`} errors={error} /></fieldset>;
}
