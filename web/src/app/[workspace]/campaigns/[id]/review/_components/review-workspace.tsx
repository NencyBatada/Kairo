"use client";

import { ShieldCheck } from "lucide-react";
import Link from "next/link";
import { useId, useRef, useState, type FormEvent, type MouseEvent } from "react";
import { useRouter } from "next/navigation";
import { ApprovalFacts } from "@/components/approval/approval-facts";
import { ApprovalPanel, type ApprovalDecision } from "@/components/approval/approval-panel";
import { EmptyState } from "@/components/empty-state";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import { assetName, languageName, type AssetSet } from "../../assets/_lib/model";
import { checkLabels, matches, snapshot, validateDecision, type ReviewDecision, type ReviewState, type Room } from "../_lib/review";
import { reviewAssets } from "../actions";
import { CheckDetails } from "./check-details";

type OpenDecision = { decision: ReviewDecision; version: Room["version"]; snapshot: string; names: string[]; locked: number };
const titles = { check: "Run Brand Guardian checks?", request: "Send assets for approval?", approve: "Approve the asset set?", override: "Override this asset's flag?", "send-back": "Send selected assets back?" };
const verbs = { check: "Run checks", request: "Send for approval", approve: "Approve assets", override: "Override flag", "send-back": "Send selected assets back" };
const consequences = {
  check: "Brand Guardian checks saved assets and can automatically save new versions of unlocked assets while fixing issues. Locked assets are kept. If every asset was already checked, all assets are checked again.",
  request: "This requests Gate 2 review of the whole saved asset set. Editing pauses until an approver decides. Flagged assets can be sent for review, but approval needs every flag passed or overridden.",
  approve: "This approves every saved asset in this campaign, including recorded overrides. The campaign moves to launch approval. Launching still needs Gate 3 approval.",
  override: "This accepts all open issues on this asset version. Your name, the time and your reason are recorded; the original score and issues stay visible.",
  "send-back": "The campaign returns to creating. Selected unlocked assets are regenerated using your comment. Locked assets are kept and must be unlocked in Creative Studio before they can change.",
};

export function ReviewWorkspace({ room, set, brandId, handle, timeZone }: { room: Room; set: AssetSet; brandId: string; handle: string; timeZone: string }) {
  const id = useId();
  const router = useRouter();
  const assets = set.groups.flatMap(group => group.assets);
  const key = snapshot(room, set);
  const [filter, setFilter] = useState("");
  const [language, setLanguage] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [opened, setOpened] = useState<OpenDecision | null>(null);
  const [lastKind, setLastKind] = useState<ReviewDecision["kind"]>("approve");
  const [reason, setReason] = useState("");
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [state, setState] = useState<ReviewState>({ status: "idle" });
  const [result, setResult] = useState<ReviewState>({ status: "idle" });
  const [pending, setPending] = useState(false);
  const [completed, setCompleted] = useState<string | null>(null);
  const commentRef = useRef<HTMLTextAreaElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const errorRef = useRef<HTMLDivElement>(null);
  const resultRef = useRef<HTMLDivElement>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const kind = opened?.decision.kind ?? lastKind;
  const hasReason = kind === "override" || kind === "send-back";
  const changed = !!opened && opened.snapshot !== key;
  const blocked = pending || completed === key;
  const reviewing = room.pendingApproval?.gate === "assets";
  const selection = assets.filter(asset => selected.includes(asset.id) && asset.canSendBack);
  const shown = assets.filter(asset => matches(asset, filter, language));
  const approvers = room.pendingApproval?.gate === "assets" ? room.pendingApproval.approvers.map(person => person.name).join(", ") : "";
  const base = `/${handle}/campaigns/${encodeURIComponent(room.id)}`;
  const selectionSummary = selection.length > 0 ? <div className="text-small"><p>{selection.length} selected across all filters, up to 50.</p><ul className="list-inside list-disc wrap-anywhere">{selection.map(asset => <li key={asset.id}>{assetName(asset)} · {languageName(asset.language)}{asset.locked ? " · Locked: will be kept" : ""}</li>)}</ul>{selection.some(asset => asset.locked) && <p className="mt-2 text-warning-strong">{selection.filter(asset => asset.locked).length} selected locked {selection.filter(asset => asset.locked).length === 1 ? "asset will" : "assets will"} be kept.</p>}</div> : null;

  function open(decision: ReviewDecision, event: MouseEvent<HTMLButtonElement>) {
    const picked = decision.kind === "send-back" ? assets.filter(asset => decision.assetIds.includes(asset.id)) : decision.kind === "override" ? assets.filter(asset => decision.assetId === asset.id) : [];
    returnFocus.current = event.currentTarget;
    setReason(drafts[decision.kind === "override" ? decision.assetId : decision.kind] ?? "");
    setState({ status: "idle" });
    setLastKind(decision.kind);
    setOpened({ decision, version: room.version, snapshot: key, names: picked.map(assetName), locked: picked.filter(asset => asset.locked).length });
  }

  function close() {
    if (pending) return;
    if (opened && hasReason) setDrafts(current => ({ ...current, [opened.decision.kind === "override" ? opened.decision.assetId : opened.decision.kind]: reason }));
    setOpened(null);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!opened || pending || changed || state.conflict) return;
    const decision = opened.decision.kind === "override" || opened.decision.kind === "send-back" ? { ...opened.decision, reason } : opened.decision;
    const invalid = validateDecision(decision);
    if (invalid) { setState(invalid); if (invalid.fieldErrors?.reason) commentRef.current?.focus(); else requestAnimationFrame(() => errorRef.current?.focus()); return; }
    setPending(true);
    setState({ status: "idle" });
    let response: ReviewState;
    try { response = await reviewAssets(brandId, room.id, opened.version, decision); }
    catch { response = { status: "error", message: "The decision could not be recorded. Your comment is still here; try again." }; }
    setPending(false);
    if (response.status !== "saved") { setState(response); requestAnimationFrame(() => response.fieldErrors?.reason ? commentRef.current?.focus() : errorRef.current?.focus()); return; }
    setCompleted(opened.snapshot);
    setResult(response);
    setDrafts(current => ({ ...current, [decision.kind === "override" ? decision.assetId : decision.kind]: "" }));
    if (decision.kind === "send-back") setSelected([]);
    returnFocus.current = resultRef.current;
    setOpened(null);
  }

  async function decideGate(decision: ApprovalDecision): Promise<ReviewState> {
    if (blocked || (decision.kind !== "approve" && decision.kind !== "send-back")) return { status: "error", message: "Reload the review before deciding." };
    const operation: ReviewDecision = decision.kind === "approve" ? { kind: "approve" } : { kind: "send-back", assetIds: selection.map(asset => asset.id), reason: decision.reason };
    setPending(true);
    let response: ReviewState;
    try { response = await reviewAssets(brandId, room.id, room.version, operation); }
    catch { response = { status: "error", message: "The decision could not be recorded. Your comment is still here; try again." }; }
    setPending(false);
    if (response.status === "saved") { setCompleted(key); setResult(response); if (decision.kind === "send-back") setSelected([]); requestAnimationFrame(() => resultRef.current?.focus()); }
    return response;
  }

  return <div className="min-w-0 space-y-8">
    <section aria-labelledby={`${id}-title`} className="space-y-4 rounded-lg border border-border bg-card p-4 md:p-5">
      <div className="flex flex-wrap items-center justify-between gap-3"><h2 id={`${id}-title`} className="font-display text-display-sm">Brand Guardian</h2>{!reviewing && <Badge variant="gate">Gate 2 · Assets</Badge>}</div>
      <p className="max-w-prose text-sm text-muted-foreground">Review the saved checks and fixes before approving the whole asset set. Scores support the decision; open blocking issues must be resolved even when the score is above the pass threshold.</p>
      <ApprovalFacts facts={[
        { label: "Set score", value: set.check.score === null ? "Not scored" : `${set.check.score} / 100` },
        { label: "Pass threshold", value: `${set.check.passScore} / 100` },
        { label: "Passed", value: String(set.check.passed) }, { label: "Flagged", value: String(set.check.flagged) },
        { label: "Overridden", value: String(set.check.overridden) }, { label: "Not checked", value: String(set.check.unchecked) },
      ]} />
      <p className="text-small text-muted-foreground">Current settings version {set.check.settingsVersion} · Automatic fix limit: {set.check.fixAttempts} per asset and check settings version. Each asset shows which settings its saved check used.</p>
      <div className="flex flex-wrap gap-x-5 gap-y-2 text-small"><Link className="inline-flex min-h-9 items-center text-primary hover:underline pointer-coarse:min-h-11" href={`${base}/assets`}>Open Creative Studio</Link><Link className="inline-flex min-h-9 items-center text-primary hover:underline pointer-coarse:min-h-11" href={`/${handle}/settings/checks`}>Check settings</Link><Button variant="link" disabled={pending} onClick={() => router.refresh()}>Reload results</Button></div>
      {room.actions.requestApprovalBlocker && !reviewing && <Alert variant="warning">{room.actions.requestApprovalBlocker}</Alert>}
      {(room.actions.approveBlocker || set.check.approveBlocker) && <Alert variant="warning">{room.actions.approveBlocker ?? set.check.approveBlocker}</Alert>}
      {!reviewing && <p className="text-small text-muted-foreground">{room.stage === "creating" ? "A marketer can run checks and request approval when every saved asset has been checked." : "These are the saved results. Review actions are available only at the matching campaign step."}</p>}
      {room.runs.some(run => ["queued", "running", "retrying"].includes(run.status)) && <Alert variant="info">Agents are working. Results update here as work finishes; progress and retry are below.</Alert>}
      <div ref={resultRef} tabIndex={-1} className="focus-visible:outline-none"><FormStatus state={result} /></div>
      {completed === key && <p role="status" className="text-small text-muted-foreground">Decision recorded. Waiting for updated results.</p>}
      <div className="sticky bottom-0 z-30 -mx-4 flex flex-wrap gap-3 border-t border-border bg-card/95 px-4 py-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur md:-mx-5 md:px-5 lg:static lg:mx-0 lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none">
        {room.actions.requestCheck && <Button variant={room.actions.requestApproval === "assets" ? "secondary" : "default"} disabled={blocked} onClick={event => open({ kind: "check" }, event)}>Run checks</Button>}
        {room.actions.requestApproval === "assets" && <Button disabled={blocked} onClick={event => open({ kind: "request" }, event)}>Send for approval</Button>}
      </div>
      {reviewing && <ApprovalPanel key={room.pendingApproval?.requestedAt} label="Gate 2 · Assets" title="Asset approval" subject="assets" status="waiting" approver={approvers || "No approver assigned. Ask an admin to check approval settings."} canDecide={set.canSendBack || room.actions.approve === "assets"} approveDisabled={blocked || room.actions.approve !== "assets" || !!set.check.approveBlocker} approveLabel="Approve assets" consequence={consequences.approve} sendBack={{ label: `Send selected assets back${selection.length ? ` (${selection.length})` : ""}`, confirmLabel: "Send selected assets back", description: consequences["send-back"], disabled: blocked || !set.canSendBack || selection.length === 0, summary: selectionSummary }} contextKey={`${key}:${selection.map(asset => asset.id).join(",")}`} reasonMaxLength={500} onDecision={decideGate}>
        <p className="text-small">Approval covers all {assets.length} saved assets, including recorded overrides. Select assets below to send them back; the remaining assets are kept.</p>
        {selectionSummary}
      </ApprovalPanel>}
      {reviewing && !set.canSendBack && room.actions.approve !== "assets" && <p className="text-small text-muted-foreground">Only an assigned approver can decide or override flags.</p>}
      {selection.length > 0 && <Button variant="ghost" disabled={blocked} onClick={() => setSelected([])}>Clear selection</Button>}
    </section>
    {assets.length === 0 ? <EmptyState icon={ShieldCheck} title="No assets to review yet" description="Brand Guardian results appear once Creative Studio has saved campaign assets." /> : <section aria-labelledby={`${id}-assets`} className="space-y-4">
      <h2 id={`${id}-assets`} className="text-base font-semibold">Asset checks</h2>
      <div className="grid max-w-160 gap-4 sm:grid-cols-2"><div className="space-y-1.5"><Label htmlFor={`${id}-state`}>Check status</Label><NativeSelect id={`${id}-state`} value={filter} onChange={event => setFilter(event.target.value)}><option value="">All statuses</option>{Object.entries(checkLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</NativeSelect></div><div className="space-y-1.5"><Label htmlFor={`${id}-language`}>Language</Label><NativeSelect id={`${id}-language`} value={language} onChange={event => setLanguage(event.target.value)}><option value="">All languages</option>{set.languages.map(code => <option key={code} value={code}>{languageName(code)}</option>)}</NativeSelect></div></div>
      <p role="status" className="text-small text-muted-foreground">Showing {shown.length} of {assets.length} assets. The set score and approval always cover all assets.</p>
      {shown.length === 0 && <p className="rounded-lg border border-border p-5 text-sm">No assets match these filters. Choose another status or language.</p>}
      <div className="grid items-start gap-5 xl:grid-cols-2">{shown.map(asset => <article key={asset.id} aria-label={`${assetName(asset)} · ${languageName(asset.language)}`} className="min-w-0 space-y-4 rounded-lg border border-border bg-card p-4 md:p-5">
        <div className="space-y-3"><h3 className="text-base font-semibold wrap-anywhere">{assetName(asset)}</h3><div className="flex flex-wrap items-center gap-2"><Badge variant={asset.checkState === "passed" ? "success" : asset.checkState === "failed" ? "destructive" : asset.checkState === "overridden" ? "warning" : "neutral"}>{checkLabels[asset.checkState] ?? asset.checkState}</Badge><span className="text-small tabular-nums">{asset.check.score === null ? "Not scored" : `${asset.check.score} / 100`}</span>{asset.locked && <Badge>Locked</Badge>}</div><p className="text-xs text-muted-foreground">{asset.campaignType === "demand_gen" ? "Demand Gen" : asset.campaignType === "shopping" ? "Shopping" : "Search"} · {languageName(asset.language)} · Version {asset.version}</p></div>
        {asset.canSendBack && <label className="flex min-h-9 items-center gap-2 text-sm pointer-coarse:min-h-11"><Checkbox checked={selection.some(entry => entry.id === asset.id)} disabled={blocked || (!selected.includes(asset.id) && selection.length >= 50)} onChange={event => setSelected(current => event.target.checked ? [...current.filter(value => value !== asset.id), asset.id] : current.filter(value => value !== asset.id))} /><span className="min-w-0 wrap-anywhere">Select {assetName(asset)} for send-back</span></label>}
        <CheckDetails asset={asset} assets={assets} brandId={brandId} campaignId={room.id} timeZone={timeZone} />
        {asset.disclosure.blocker && <Alert variant="destructive">{asset.disclosure.blocker}</Alert>}
        {asset.canOverride && <Button variant="secondary" disabled={blocked} onClick={event => open({ kind: "override", assetId: asset.id, assetVersion: asset.version, reason: "" }, event)}>Override flag</Button>}
        {asset.check.settingsVersion !== null && String(asset.check.settingsVersion) !== String(set.check.settingsVersion) && <Alert variant="info">These saved checks used settings version {asset.check.settingsVersion}. Current settings are version {set.check.settingsVersion}. Run checks again from the creating step to use the new settings.</Alert>}
      </article>)}</div>
    </section>}
    <Sheet open={opened !== null} onOpenChange={next => !next && close()}><SheetContent side="bottom" initialFocus={hasReason ? commentRef : confirmRef} finalFocus={returnFocus}>
      <form noValidate onSubmit={submit} className="space-y-5 p-4 md:p-5">
        <div className="space-y-2 pr-10"><SheetTitle>{titles[kind]}</SheetTitle><SheetDescription>{consequences[kind]}</SheetDescription></div>
        {opened && opened.names.length > 0 && <ul className="list-inside list-disc text-small wrap-anywhere">{opened.names.map((name, index) => <li key={index}>{name}</li>)}</ul>}
        {!!opened?.locked && <Alert variant="warning">{opened.locked} selected locked {opened.locked === 1 ? "asset will" : "assets will"} be kept.</Alert>}
        {hasReason && <div className="space-y-2"><Label htmlFor={`${id}-reason`}>{kind === "override" ? "Reason for override" : "What needs to change"}</Label><p id={`${id}-reason-help`} className="text-small text-muted-foreground">{kind === "override" ? "3" : "2"} to 500 characters. This is recorded in the activity log.</p><Textarea id={`${id}-reason`} ref={commentRef} value={reason} onChange={event => setReason(event.target.value)} maxLength={500} disabled={pending} aria-invalid={!!state.fieldErrors?.reason || undefined} aria-describedby={`${id}-reason-help ${id}-reason-error`} /><p id={`${id}-reason-error`} className="text-small text-destructive-strong">{state.fieldErrors?.reason?.join(" ")}</p></div>}
        {(changed || state.conflict) && <Alert variant="warning">The campaign or its checks changed. Your comment is kept. Close this sheet, reload the results and review the current versions before deciding.</Alert>}
        <div ref={errorRef} tabIndex={-1} className="focus-visible:outline-none"><FormStatus state={state} />{Object.entries(state.fieldErrors ?? {}).filter(([field]) => field !== "reason" && field !== "comment").map(([field, errors]) => <p key={field} className="text-small text-destructive-strong">{errors.join(" ")}</p>)}</div>
        <div className="flex flex-col-reverse gap-3 sm:flex-row"><Button variant="secondary" disabled={pending} onClick={close}>Cancel</Button><Button type="submit" ref={confirmRef} disabled={changed || !!state.conflict} pending={pending}>{verbs[kind]}</Button></div>
      </form>
    </SheetContent></Sheet>
  </div>;
}
