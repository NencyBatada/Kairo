"use client";

import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { money, type BriefDetail, type ProofResult } from "../../brief/_lib/model";
import { saveLaunchSettings } from "../actions";
import { bidNames, channelNames, readSettings, regions, toDraft, type Draft, type LaunchState, type PlanView } from "../_lib/model";

export function LaunchSettings({ view, brief, proof, brandId, campaignId, editable, onDirty, onSaved }: { view: PlanView; brief: BriefDetail; proof: ProofResult; brandId: string; campaignId: string; editable: boolean; onDirty: (value: boolean) => void; onSaved: (view: PlanView) => void }) {
  const router = useRouter();
  const id = useId();
  const plan = view.plan!;
  const types = brief.brief?.campaignTypes ?? [];
  const [local, setLocal] = useState<{ baseline: PlanView; draft: Draft } | null>(null);
  const [state, setState] = useState<LaunchState>({ status: "idle" });
  const [pending, setPending] = useState(false);
  const [discard, setDiscard] = useState(false);
  const [notice, setNotice] = useState(false);
  const summary = useRef<HTMLDivElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const dirty = local !== null;
  const draft = local?.draft ?? toDraft(plan, types, proof);
  const stale = !!local && (String(local.baseline.version) !== String(view.version) || local.baseline.hash !== view.hash);
  const errors = state.fieldErrors ?? {};
  const blocked = pending || !editable || stale || !!state.conflict;
  useEffect(() => { onDirty(dirty || pending); }, [dirty, pending, onDirty]);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    const follow = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
      const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>("a[href]") : null;
      if (!link || link.target === "_blank" || link.hasAttribute("download")) return;
      const url = new URL(link.href, location.href);
      if (url.origin === location.origin && url.pathname === location.pathname && url.search === location.search && url.hash) return;
      event.preventDefault(); event.stopPropagation(); setNotice(true); requestAnimationFrame(() => summary.current?.focus());
    };
    window.addEventListener("beforeunload", warn); document.addEventListener("click", follow, true);
    return () => { window.removeEventListener("beforeunload", warn); document.removeEventListener("click", follow, true); };
  }, [dirty]);
  function update(next: Draft) { setLocal(JSON.stringify(next) === JSON.stringify(toDraft(plan, types, proof)) ? null : { baseline: local?.baseline ?? view, draft: next }); setState({ status: "idle" }); }
  function changeChannel(index: number, key: keyof Draft["channels"][number], value: string) { update({ ...draft, channels: draft.channels.map((row, i) => i === index ? { ...row, [key]: value, ...(key === "biddingStrategy" && value === "maximize_clicks" ? { target: "" } : {}) } : row) }); }
  const fieldId = (field: string) => `${id}-${field}`;
  function field(name: string, label: string, help: string, control: (props: { id: string; "aria-invalid": boolean; "aria-describedby": string }) => ReactNode) {
    return <div key={name} className="min-w-0 space-y-1.5"><label className="text-small font-semibold" htmlFor={fieldId(name)}>{label}</label><p id={`${fieldId(name)}-help`} className="text-small text-muted-foreground">{help}</p>{control({ id: fieldId(name), "aria-invalid": !!errors[name], "aria-describedby": `${fieldId(name)}-help ${fieldId(name)}-error` })}{errors[name] && <p id={`${fieldId(name)}-error`} className="text-small text-destructive-strong">{errors[name].join(" ")}</p>}</div>;
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked) return;
    const checked = readSettings(draft, local?.baseline.plan ?? plan, brief, proof);
    if (!checked.settings) { setState({ status: "error", message: "Check the highlighted launch settings.", fieldErrors: checked.errors }); requestAnimationFrame(() => summary.current?.focus()); return; }
    setPending(true);
    let response: LaunchState;
    try { response = await saveLaunchSettings(brandId, campaignId, local?.baseline.version ?? view.version, draft); }
    catch { response = { status: "error", message: "The settings could not be saved. Your edits are kept; try again." }; }
    setPending(false); setState(response);
    if (response.status === "saved" && response.view) { setLocal(null); onSaved(response.view); onDirty(false); setNotice(false); }
    requestAnimationFrame(() => summary.current?.focus());
  }
  if (!editable && !dirty) return <p className="text-small text-muted-foreground">A marketer can change launch settings when this campaign is ready to launch and no approval is pending. The saved settings are shown in the summary.</p>;
  return <section aria-labelledby={`${id}-title`} className="space-y-5 rounded-lg border bg-card p-5 sm:p-6"><h2 id={`${id}-title`} className="text-xl font-semibold">Set up the launch</h2><p className="max-w-prose text-small text-muted-foreground">Divide the approved {money(plan.budget, plan.currency)} across its campaign types. Saving settings does not launch or spend. Review the saved summary, then request approval.</p>
    <form noValidate onSubmit={submit} className="min-w-0 space-y-6">
      <div ref={summary} tabIndex={-1} className="space-y-3 outline-none"><FormStatus state={state} />{notice && <Alert variant="warning">Your unsaved launch settings are kept. Save or discard them before leaving.</Alert>}{(stale || state.conflict) && <Alert variant="warning">The campaign changed. Your edits are kept. Copy anything you need, then discard and reload to review the current plan.</Alert>}{!editable && dirty && <Alert variant="warning">Launch settings are now read-only. Your unsaved draft is kept so you can copy it.</Alert>}{Object.keys(errors).length > 0 && <ul className="list-inside list-disc text-small">{Object.entries(errors).map(([key, messages]) => <li key={key}><a href={`#${fieldId(key)}`} className="text-primary underline underline-offset-4" onClick={event => { event.preventDefault(); const element = document.getElementById(fieldId(key)); element?.focus(); element?.scrollIntoView?.({ block: "center" }); }}>{messages.join(" ")}</a></li>)}</ul>}</div>
      <fieldset disabled={blocked} className="min-w-0 space-y-6"><legend className="sr-only">Launch settings</legend>
        <div id={fieldId("settings")} tabIndex={-1} className="space-y-5 outline-none"><div id={fieldId("channels")} tabIndex={-1} className="space-y-5 outline-none">{draft.channels.map((row, index) => <fieldset key={row.campaignType} className="min-w-0 space-y-4 rounded-md border p-4"><legend className="px-1 text-sm font-semibold">{channelNames[row.campaignType]}</legend><div className="grid gap-4 md:grid-cols-2">{field(`channels.${index}.budget`, `${channelNames[row.campaignType]} budget (${plan.currency})`, "Total for the entire campaign window, with up to two decimal places.", props => <Input {...props} inputMode="decimal" value={row.budget} onChange={event => changeChannel(index, "budget", event.target.value)} />)}{field(`channels.${index}.biddingStrategy`, `${channelNames[row.campaignType]} bidding`, "Choose how Google Ads bids for this channel.", props => <NativeSelect {...props} value={row.biddingStrategy} onChange={event => changeChannel(index, "biddingStrategy", event.target.value)}>{Object.entries(bidNames).filter(([key]) => row.campaignType !== "shopping" || key !== "target_cpa").map(([key, label]) => <option key={key} value={key}>{label}</option>)}</NativeSelect>)}</div>{row.biddingStrategy !== "maximize_clicks" && field(`channels.${index}.target`, `${channelNames[row.campaignType]} bid target`, row.biddingStrategy === "target_roas" ? "Return as a ratio: 4 means 400% return on ad spend." : `Cost per acquisition in ${plan.currency}.`, props => <Input {...props} inputMode="decimal" value={row.target} onChange={event => changeChannel(index, "target", event.target.value)} />)}</fieldset>)}</div>
        {types.includes("shopping") && field("salesCountry", "Shopping sales country", "Two-letter country code, such as IN for India. This must match the connected Merchant Center catalogue.", props => <Input {...props} value={draft.salesCountry} maxLength={2} onChange={event => update({ ...draft, salesCountry: event.target.value })} />)}
        {field("audienceIds", `Google Ads audience lists${types.includes("demand_gen") ? "" : " (optional)"}`, "User-list IDs, one per line. Demand Gen requires at least one list; use lists from the connected Google Ads account.", props => <Textarea {...props} rows={3} value={draft.audienceIds} onChange={event => update({ ...draft, audienceIds: event.target.value })} />)}
        <fieldset className="min-w-0 space-y-4"><legend className="mb-2 text-base font-semibold">Google Ads locations</legend><p className="max-w-prose text-small text-muted-foreground">Map the approved regions to their Google Ads location IDs. Test regions are targeted; holdout regions are excluded so we can measure extra results. These mappings do not change the approved test plan.</p><div className="grid gap-5 md:grid-cols-2">{regions(proof).map(region => field(`geo.${region.id}`, `${region.name} · ${proof.plan?.holdouts.some(item => item.id === region.id) ? "Holdout (excluded)" : "Test region"}`, "Positive Google Ads location ID.", props => <Input {...props} inputMode="numeric" value={draft.geoTargetIds[region.id] ?? ""} onChange={event => update({ ...draft, geoTargetIds: { ...draft.geoTargetIds, [region.id]: event.target.value } })} />))}</div></fieldset></div>
      </fieldset>
      <div className="flex flex-wrap gap-3"><Button type="submit" variant="secondary" pending={pending} disabled={blocked} className="max-sm:w-full">Save launch settings</Button>{dirty && <Button variant="secondary" disabled={pending} onClick={() => setDiscard(true)}>Discard and reload</Button>}</div>
    </form>
    <AlertDialog open={discard} onOpenChange={setDiscard}><AlertDialogContent initialFocus={cancel}><AlertDialogTitle>Discard unsaved launch settings?</AlertDialogTitle><AlertDialogDescription>Your edits are removed and the latest saved campaign is loaded.</AlertDialogDescription><AlertDialogFooter><AlertDialogCancel ref={cancel}>Cancel</AlertDialogCancel><Button variant="destructive-solid" onClick={() => { setLocal(null); setState({ status: "idle" }); setNotice(false); setDiscard(false); onDirty(false); router.refresh(); }}>Discard and reload</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </section>;
}
