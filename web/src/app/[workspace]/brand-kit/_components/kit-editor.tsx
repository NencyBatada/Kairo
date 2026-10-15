"use client";

import { Sparkles } from "lucide-react";
import { useActionState, useState } from "react";

import { FormStatus } from "@/components/form-status";
import { FieldError } from "@/components/field-error";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { idle } from "@/lib/forms";

import { KitConfirm } from "./kit-confirm";
import { ObjectRows, TextField } from "./kit-fields";
import { KitSources } from "./kit-sources";
import type { KitAction, KitActionState, KitContent, KitFile, KitVersion } from "./types";

export function KitEditor({ version, draftRevision, files, canEdit, save, approve, discard, readOnlyMessage }: {
  version: KitVersion | null; draftRevision: number | string; files: KitFile[]; canEdit: boolean;
  save: KitAction; approve: KitAction; discard: KitAction; readOnlyMessage?: string;
}) {
  const [content, setContent] = useState<KitContent>(version?.content ?? {});
  const [revision, setRevision] = useState(Number(draftRevision));
  const serverToken = JSON.stringify([version?.number, version?.status, version?.revision, draftRevision]);
  const [seenServerToken, setSeenServerToken] = useState(serverToken);
  const [stale, setStale] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [state, submit, pending] = useActionState(async (previous: KitActionState, data: FormData) => {
    const result = await save(previous, data);
    if (result.version) {
      setContent(result.version.content);
      setRevision(Number(result.version.revision));
      setDirty(false);
      setStale(false);
    }
    return result;
  }, idle);
  if (serverToken !== seenServerToken) {
    setSeenServerToken(serverToken);
    if (dirty) setStale(true);
    else {
      setContent(version?.content ?? {});
      setRevision(Number(draftRevision));
      setStale(false);
    }
  }
  const change = (next: KitContent) => { setContent(next); setDirty(true); };
  const errors = state.fieldErrors ?? {};
  const errorsFor = (path: string) => Object.entries(errors).filter(([key]) => key === path || key.startsWith(`${path}[`) || key.startsWith(`${path}.`)).flatMap(([, messages]) => messages);
  const voice = content.voice ?? {};
  const rules = content.rules ?? {};
  const attached = [...files, ...(version?.files ?? []).filter((file) => !files.some((candidate) => candidate.id === file.id))];
  const voiceLines = [
    ["traits", "Voice traits"], ["dos", "Do"], ["donts", "Don't"], ["examples", "Example phrases"],
  ] as const;

  return <section aria-label="Brand Kit content" className="flex flex-col gap-4">
    <div className="flex flex-wrap items-center gap-3">
      <h2 className="text-base font-semibold">{version ? `Version ${version.number}` : "Create your Brand Kit"}</h2>
      <Badge variant={version?.status === "approved" ? "success" : "neutral"}>{stale ? "Local copy" : version?.status === "approved" ? "Approved" : version?.status === "superseded" ? "Superseded" : "Draft"}</Badge>
      {version?.origin === "drafted" && !dirty && <span className="inline-flex items-center gap-1.5 text-small text-muted-foreground"><Sparkles className="size-4" aria-hidden="true" />Drafted by Kairo</span>}
    </div>
    {!canEdit && <Alert>{readOnlyMessage ?? "Only admins can edit and approve the Brand Kit."}</Alert>}
    {canEdit && <p className="text-small text-muted-foreground">Save your changes as a draft, then approve the saved version. Agents keep using the approved kit until then.</p>}
    {stale && <Alert variant="warning">
      <p>The draft changed elsewhere. Your local edits have been kept. Copy anything you need before loading the latest version.</p>
      <Button variant="secondary" className="mt-2 self-start" onClick={() => { setContent(version?.content ?? {}); setRevision(Number(draftRevision)); setDirty(false); setStale(false); }}>Load latest version</Button>
    </Alert>}
    <form action={submit} className="flex flex-col gap-5">
      <input type="hidden" name="revision" value={revision} />
      <input type="hidden" name="content" value={JSON.stringify(content)} />
      <fieldset disabled={!canEdit || pending || stale} className="flex min-w-0 flex-col gap-5">
        <Card><CardHeader><CardTitle>Voice</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">
          <TextField label="Voice summary" errors={errorsFor("content.voice.summary")} value={voice.summary ?? ""} maxLength={500} multiline onChange={(summary) => change({ ...content, voice: { ...voice, summary } })} />
          <KitSources sources={voice.sources} />
          <div className="grid gap-5 md:grid-cols-2">{voiceLines.map(([key, label]) => <TextField key={key} label={label} errors={errorsFor(`content.voice.${key}`)} multiline help="One entry per line." value={(voice[key] ?? []).join("\n")} onChange={(value) => change({ ...content, voice: { ...voice, [key]: value.split("\n") } })} />)}</div>
        </CardContent></Card>
        <Card><CardHeader><CardTitle>Visuals</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">
          <ObjectRows path="content.colours" errors={errors} name="Colours" singular="colour" rows={content.colours ?? []} fields={[{ key: "name", label: "Colour name", maxLength: 50 }, { key: "hex", label: "Colour code", maxLength: 7 }, { key: "role", label: "Colour role", options: ["primary", "secondary", "accent", "background", "text"] }]} empty={{ name: "", hex: "", role: "primary" }} limit={12} canEdit={canEdit} onChange={(colours) => change({ ...content, colours })} />
          <div className="flex flex-col gap-1.5"><Label htmlFor="kit-logo">Logo file</Label><NativeSelect id="kit-logo" aria-invalid={errorsFor("content.logo.fileId").length ? true : undefined} aria-describedby="kit-logo-error" value={content.logo?.fileId ?? ""} onChange={(e) => change({ ...content, logo: { ...content.logo, fileId: e.target.value || null } })}>
            <option value="">No logo selected</option>{attached.filter((file) => file.kind === "logo").map((file) => <option key={file.id} value={file.id}>{file.fileName}{file.deleted ? " (archived)" : ""}</option>)}
          </NativeSelect></div>
          <FieldError id="kit-logo-error" errors={errorsFor("content.logo.fileId")} />
          <TextField label="Logo source URL" errors={errorsFor("content.logo.sourceUrl")} value={content.logo?.sourceUrl ?? ""} maxLength={2000} onChange={(sourceUrl) => change({ ...content, logo: { ...content.logo, sourceUrl: sourceUrl || null } })} />
        </CardContent></Card>
        <Card><CardHeader><CardTitle>Messages and products</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">
          <ObjectRows path="content.keyMessages" errors={errors} name="Key messages" singular="key message" rows={content.keyMessages ?? []} fields={[{ key: "text", label: "Key message", maxLength: 200 }]} empty={{ text: "" }} limit={20} canEdit={canEdit} onChange={(keyMessages) => change({ ...content, keyMessages })} />
          <ObjectRows path="content.productCategories" errors={errors} name="Product categories" singular="product category" rows={content.productCategories ?? []} fields={[{ key: "name", label: "Category name", maxLength: 100 }]} empty={{ name: "" }} limit={50} canEdit={canEdit} onChange={(productCategories) => change({ ...content, productCategories })} />
        </CardContent></Card>
        <Card><CardHeader><CardTitle>Rules every agent follows</CardTitle></CardHeader><CardContent className="flex flex-col gap-5">
          <TextField label="Banned words" errors={errorsFor("content.rules.bannedWords")} multiline help="One word or phrase per line." value={(rules.bannedWords ?? []).join("\n")} onChange={(value) => change({ ...content, rules: { ...rules, bannedWords: value.split("\n") } })} />
          <TextField label="Tone rules" errors={errorsFor("content.rules.tone")} multiline help="One rule per line." value={(rules.tone ?? []).join("\n")} onChange={(value) => change({ ...content, rules: { ...rules, tone: value.split("\n") } })} />
          <ObjectRows path="content.rules.requiredDisclaimers" errors={errors} name="Required disclaimers" singular="disclaimer" rows={rules.requiredDisclaimers ?? []} fields={[{ key: "text", label: "Disclaimer", maxLength: 500 }, { key: "when", label: "When to include", maxLength: 200 }]} empty={{ text: "", when: "" }} limit={20} canEdit={canEdit} onChange={(requiredDisclaimers) => change({ ...content, rules: { ...rules, requiredDisclaimers } })} />
          <ObjectRows path="content.rules.competitors" errors={errors} name="Competitors" singular="competitor" rows={rules.competitors ?? []} fields={[{ key: "name", label: "Competitor name", maxLength: 100 }, { key: "policy", label: "Mention policy", options: ["never", "neutral"] }]} empty={{ name: "", policy: "never" }} limit={50} canEdit={canEdit} onChange={(competitors) => change({ ...content, rules: { ...rules, competitors } })} />
        </CardContent></Card>
        <Card><CardHeader><CardTitle>Guidelines</CardTitle></CardHeader><CardContent className="flex flex-col gap-3">
          <p className="text-small text-muted-foreground">Select the uploaded guidelines to include in this version.</p>
          {attached.filter((file) => file.kind === "guideline").map((file) => <label key={file.id} className="flex min-h-6 items-center gap-2 text-sm wrap-anywhere pointer-coarse:min-h-11">
            <Checkbox aria-invalid={errorsFor("content.guidelineFileIds").length ? true : undefined} aria-describedby="kit-guidelines-error" checked={(content.guidelineFileIds ?? []).includes(file.id)} onChange={(e) => change({ ...content, guidelineFileIds: e.target.checked ? [...(content.guidelineFileIds ?? []), file.id] : (content.guidelineFileIds ?? []).filter((id) => id !== file.id) })} />
            {file.fileName}{file.deleted ? " (archived)" : ""}
          </label>)}
          <FieldError id="kit-guidelines-error" errors={errorsFor("content.guidelineFileIds")} />
          {attached.filter((file) => file.kind === "guideline").length === 0 && <p className="text-small text-muted-foreground">Upload guideline files below, then select them here.</p>}
        </CardContent></Card>
      </fieldset>
      {state.fieldErrors && <Alert variant="destructive"><p>Review the marked fields and save again.</p><ul className="list-inside list-disc">{Object.entries(state.fieldErrors).map(([field, errors]) => <li key={field}>{errors.join(" ")}</li>)}</ul></Alert>}
      <FormStatus state={state} />
      {canEdit && <div className="flex flex-col items-start gap-2"><Button type="submit" pending={pending} disabled={stale} className="max-sm:w-full">Save draft</Button>{dirty && <p className="text-small text-muted-foreground">You have unsaved changes. Save before approving.</p>}</div>}
    </form>
    {canEdit && Number(draftRevision) > 0 && <div className="flex flex-wrap gap-3">
      <KitConfirm label="Approve version" title={`Approve version ${version?.number}?`} description="Every agent will use this saved Brand Kit. The previous approved kit will move to version history." action={approve} revision={revision} disabled={dirty || pending || stale} />
      <KitConfirm label="Discard draft" title="Discard this draft?" description="The draft and any unsaved edits will be discarded. The approved kit stays available to agents." action={discard} revision={revision} destructive disabled={pending || stale} />
    </div>}
  </section>;
}
