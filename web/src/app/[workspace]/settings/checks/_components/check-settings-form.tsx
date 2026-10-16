"use client";

import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { saveCheckSettings } from "../actions";
import { readSettings, severityNames, targetNames, toDraft, type Draft, type Settings, type SettingsState } from "../_lib/settings";

export function CheckSettingsForm({ settings, brandId, canEdit, timeZone }: { settings: Settings; brandId: string; canEdit: boolean; timeZone: string }) {
  const id = useId();
  const router = useRouter();
  const [saved, setSaved] = useState<{ source: Settings["version"]; settings: Settings } | null>(null);
  const current = saved && String(saved.source) === String(settings.version) ? saved.settings : settings;
  const [local, setLocal] = useState<{ baseline: Settings; draft: Draft } | null>(null);
  const draft = local?.draft ?? toDraft(current);
  const dirty = local !== null;
  const stale = !!local && String(local.baseline.version) !== String(current.version);
  const [state, setState] = useState<SettingsState>({ status: "idle" });
  const [pending, setPending] = useState(false);
  const [discard, setDiscard] = useState(false);
  const [removeIndex, setRemoveIndex] = useState<number | null>(null);
  const [notice, setNotice] = useState(false);
  const summaryRef = useRef<HTMLDivElement>(null);
  const noticeRef = useRef<HTMLDivElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const readOnly = !canEdit && !dirty;
  const errors = state.fieldErrors ?? {};
  const fieldId = (field: string) => `${id}-${field.replace(/[^\w-]/g, "-")}`;

  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    const follow = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
      const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>("a[href]") : null;
      if (!link || link.target === "_blank" || link.hasAttribute("download")) return;
      const url = new URL(link.href, location.href);
      if (url.origin === location.origin && url.pathname === location.pathname && url.search === location.search && url.hash) return;
      event.preventDefault(); event.stopPropagation(); setNotice(true); requestAnimationFrame(() => noticeRef.current?.focus());
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", follow, true);
    return () => { window.removeEventListener("beforeunload", warn); document.removeEventListener("click", follow, true); };
  }, [dirty]);

  function update(next: Draft) {
    setLocal(JSON.stringify(next) === JSON.stringify(toDraft(current)) ? null : { baseline: local?.baseline ?? current, draft: next });
    setState({ status: "idle" });
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending || !canEdit || stale || state.conflict || !dirty) return;
    const invalid = readSettings(draft, local?.baseline ?? current);
    if (Object.keys(invalid.errors).length) { setState({ status: "error", message: "Check the highlighted fields.", fieldErrors: invalid.errors }); requestAnimationFrame(() => summaryRef.current?.focus()); return; }
    const baseline = local?.baseline ?? current;
    setPending(true);
    let response: SettingsState;
    try { response = await saveCheckSettings(brandId, baseline.version, draft); }
    catch { response = { status: "error", message: "The settings could not be saved. Your edits are still here; try again." }; }
    setPending(false);
    setState(response);
    if (response.status === "saved" && response.settings) { setSaved({ source: settings.version, settings: response.settings }); setLocal(null); setNotice(false); }
    requestAnimationFrame(() => summaryRef.current?.focus());
  }

  function error(field: string) {
    return errors[field] ? <p id={`${fieldId(field)}-error`} className="text-small text-destructive-strong">{errors[field].join(" ")}</p> : null;
  }

  return <form ref={formRef} noValidate onSubmit={submit} className="min-w-0 space-y-8">
    <div className="space-y-3"><h2 className="font-display text-display-sm">Brand Guardian settings</h2><p className="max-w-prose text-sm text-muted-foreground">Rules for checking text, images and video across this workspace. Saved checks keep their original results. Run checks again in a campaign to apply changed settings.</p><p className="text-xs text-muted-foreground">Settings version {current.version}{current.updatedAt ? ` · Updated ${new Intl.DateTimeFormat("en-IN", { timeZone, dateStyle: "medium", timeStyle: "short" }).format(new Date(current.updatedAt))}` : " · Using defaults"}</p></div>
    {!canEdit && <Alert variant="info">Only workspace admins can change these settings.{dirty ? " Your unsaved draft is kept so you can copy it before discarding." : " You can review the current rules below."}</Alert>}
    {stale && <Alert variant="warning">Someone else changed these settings. Your draft is kept. Copy any edits you need, then discard and reload to review the current rules.</Alert>}
    {notice && <div ref={noticeRef} tabIndex={-1} className="outline-none"><Alert variant="warning">Your unsaved settings are still here. Save or discard them before following this link.</Alert></div>}
    <div ref={summaryRef} tabIndex={-1} className="space-y-2 outline-none"><FormStatus state={state} />{Object.keys(errors).length > 0 && <ul className="list-inside list-disc space-y-1 text-small">{Object.entries(errors).map(([field, messages]) => <li key={field}><a className="text-primary underline underline-offset-4" href={`#${fieldId(field)}`} onClick={event => { event.preventDefault(); const element = document.getElementById(fieldId(field)); if (element) { element.focus(); element.scrollIntoView?.({ block: "center" }); } }}>{messages.join(" ")}</a></li>)}</ul>}{state.conflict && <Alert variant="warning">Your edits are kept. Discard and reload before trying to save again.</Alert>}</div>
    <section aria-labelledby={`${id}-thresholds`} className="max-w-160 space-y-4"><h3 id={`${id}-thresholds`} className="text-base font-semibold">Score and automatic fixes</h3><p className="text-small text-muted-foreground">Scores start at 100. Each blocking issue subtracts 30 and each warning subtracts 10, down to zero. Any blocking issue fails the asset regardless of its score.</p>
      <div className="grid gap-5 sm:grid-cols-2">{([['passScore', 'Pass score', '0 to 100. An asset must also have no blocking issues.', 100], ['fixAttempts', 'Automatic fix attempts', '0 to 5 per asset and settings version. Use 0 to check without automatic fixes. Locked assets are kept.', 5]] as const).map(([field, label, help, max]) => <div key={field} className="space-y-1.5"><Label htmlFor={readOnly ? undefined : fieldId(field)}>{label}</Label><p id={`${fieldId(field)}-help`} className="text-small text-muted-foreground">{help}</p>{readOnly ? <p className="font-medium tabular-nums">{draft[field]}</p> : <Input id={fieldId(field)} type="number" inputMode="numeric" min={0} max={max} step={1} value={draft[field]} disabled={pending} onChange={event => update({ ...draft, [field]: event.target.value })} aria-invalid={!!errors[field] || undefined} aria-describedby={`${fieldId(field)}-help ${fieldId(field)}-error`} />}{error(field)}</div>)}</div>
    </section>
    <section aria-labelledby={`${id}-rules`} className="space-y-4"><h3 id={`${id}-rules`} className="text-base font-semibold">Built-in rules</h3><p className="text-small text-muted-foreground">Block: the issue must be resolved or explicitly overridden by an approver where allowed. Warn: reduces the score. Off: skips that check. Fixed rules always block.</p><div className="divide-y divide-border rounded-lg border border-border bg-card">{current.rules.map(rule => <div key={rule.id} className="grid min-w-0 gap-3 p-4 md:grid-cols-[minmax(0,1fr)_10rem] md:p-5"><div className="min-w-0 space-y-2"><p className="text-sm font-semibold wrap-anywhere">{rule.name}</p><p className="max-w-prose text-small text-muted-foreground">{rule.description}</p><p className="text-xs text-muted-foreground">{rule.appliesTo.map(target => targetNames[target] ?? target).join(", ")} · {rule.overridable ? "Approver can override a flagged asset with a reason" : "Cannot be overridden"}</p></div><div className="space-y-1.5">{rule.adjustable && !readOnly ? <><Label htmlFor={fieldId(`rules.${rule.id}`)}>Severity for {rule.name}</Label><NativeSelect id={fieldId(`rules.${rule.id}`)} value={draft.rules[rule.id] ?? rule.severity} disabled={pending} onChange={event => update({ ...draft, rules: { ...draft.rules, [rule.id]: event.target.value } })} aria-invalid={!!errors[`rules.${rule.id}`] || undefined} aria-describedby={`${fieldId(`rules.${rule.id}`)}-error`}>{Object.entries(severityNames).map(([value, name]) => <option key={value} value={value}>{name}</option>)}</NativeSelect>{error(`rules.${rule.id}`)}</> : <div className="space-y-2"><Badge variant={rule.severity === "block" ? "destructive" : rule.severity === "warn" ? "warning" : "neutral"}>{severityNames[rule.severity] ?? rule.severity}</Badge>{!rule.adjustable && <p className="text-xs text-muted-foreground">Fixed rule · Cannot be changed</p>}</div>}</div></div>)}</div></section>
    <section aria-labelledby={`${id}-custom`} className="space-y-4"><h3 id={`${id}-custom`} className="text-base font-semibold">Custom rules</h3><p className="text-small text-muted-foreground">Add up to 20 rules in plain language. Each rule applies to text, images, video or all assets. To stop using a custom rule, remove it and save.</p>{error("customRules")}
      {draft.customRules.length === 0 && <p className="rounded-lg border border-border bg-muted p-4 text-small">No custom rules. Built-in checks and the Brand Kit still apply.</p>}
      <div id={fieldId("customRules")} tabIndex={-1} className="space-y-4 outline-none">{draft.customRules.map((rule, index) => {
        const prefix = `customRules[${index}]`;
        const change = (field: "text" | "appliesTo" | "severity", value: string) => update({ ...draft, customRules: draft.customRules.map((entry, row) => row === index ? { ...entry, [field]: value } : entry) });
        return <fieldset key={rule.id ?? `new-${index}`} className="max-w-160 min-w-0 space-y-4 rounded-lg border border-border bg-card p-4 md:p-5"><legend className="px-1 text-sm font-semibold">Custom rule {index + 1}</legend>{readOnly ? <><p className="wrap-anywhere text-sm">{rule.text}</p><p className="text-small text-muted-foreground">{targetNames[rule.appliesTo]} · {severityNames[rule.severity]}</p></> : <><div className="space-y-1.5"><Label htmlFor={fieldId(`${prefix}.text`)}>Rule {index + 1}</Label><p id={`${fieldId(`${prefix}.text`)}-help`} className="text-small text-muted-foreground">3 to 200 characters. Describe what an asset must do or avoid.</p><Textarea id={fieldId(`${prefix}.text`)} value={rule.text} maxLength={200} disabled={pending} onChange={event => change("text", event.target.value)} aria-invalid={!!errors[`${prefix}.text`] || undefined} aria-describedby={`${fieldId(`${prefix}.text`)}-help ${fieldId(`${prefix}.text`)}-error`} />{error(`${prefix}.text`)}{error(`${prefix}.id`)}</div><div className="grid gap-4 sm:grid-cols-2">{([['appliesTo', 'Applies to', targetNames], ['severity', 'Severity', { block: 'Block', warn: 'Warn' }]] as const).map(([field, label, options]) => <div key={field} className="space-y-1.5"><Label htmlFor={fieldId(`${prefix}.${field}`)}>{label} for rule {index + 1}</Label><NativeSelect id={fieldId(`${prefix}.${field}`)} value={rule[field]} disabled={pending} onChange={event => change(field, event.target.value)} aria-invalid={!!errors[`${prefix}.${field}`] || undefined} aria-describedby={`${fieldId(`${prefix}.${field}`)}-error`}>{Object.entries(options).map(([value, name]) => <option key={value} value={value}>{name}</option>)}</NativeSelect>{error(`${prefix}.${field}`)}</div>)}</div><Button variant="destructive" disabled={pending} onClick={() => setRemoveIndex(index)}>Remove custom rule {index + 1}</Button></>}</fieldset>;
      })}</div>
      {!readOnly && <Button variant="secondary" disabled={pending || draft.customRules.length >= 20} onClick={() => update({ ...draft, customRules: [...draft.customRules, { id: null, text: "", appliesTo: "all", severity: "block" }] })}>Add custom rule</Button>}
    </section>
    {(canEdit || dirty) && <div className="sticky bottom-0 z-30 -mx-4 flex flex-wrap gap-3 border-t border-border bg-card/95 px-4 py-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur md:-mx-6 md:px-6 lg:static lg:mx-0 lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none"><Button type="submit" disabled={!canEdit || !dirty || stale || !!state.conflict} pending={pending} className="max-sm:w-full">Save check settings</Button>{dirty && <Button variant="secondary" disabled={pending} onClick={() => setDiscard(true)}>Discard and reload</Button>}{dirty && <p className="self-center text-small text-muted-foreground">Unsaved changes</p>}</div>}
    <AlertDialog open={discard || removeIndex !== null} onOpenChange={next => { if (!next) { setDiscard(false); setRemoveIndex(null); } }}><AlertDialogContent initialFocus={cancelRef}>
      <AlertDialogTitle>{discard ? "Discard unsaved check settings?" : `Remove custom rule ${(removeIndex ?? 0) + 1}?`}</AlertDialogTitle><AlertDialogDescription>{discard ? "Your unsaved edits are removed. The latest saved settings will be loaded." : "This rule is removed from your draft. Save the settings to stop using it in future checks."}</AlertDialogDescription><AlertDialogFooter><AlertDialogCancel ref={cancelRef}>Cancel</AlertDialogCancel><Button variant="destructive-solid" onClick={() => { if (discard) { setLocal(null); setSaved(null); setState({ status: "idle" }); setNotice(false); router.refresh(); } else update({ ...draft, customRules: draft.customRules.filter((_, index) => index !== removeIndex) }); setDiscard(false); setRemoveIndex(null); }}> {discard ? "Discard and reload" : "Remove rule"}</Button></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </form>;
}
