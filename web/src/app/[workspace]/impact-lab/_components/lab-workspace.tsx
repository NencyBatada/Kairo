"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, useTransition } from "react";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { REQUEST_HEADER } from "@/lib/http-names";
import { problemToFormState } from "@/lib/forms";
import { changeAnalysis } from "../actions";
import { date, grainNames, label, setupErrors, setupOf, sourceNames, stateNames, validId, validVersion, sampleCsv, type Analysis, type LabState, type Operation, type Selection, type Setup } from "../_lib/model";
import { actionClass, control, SetupForm } from "./setup-form";
import { ComparisonPicker } from "./comparison-picker";
import { AnalysisResult, downloadFile } from "./analysis-result";

type Step = "setup" | "data" | "comparison" | "result";
type Command = { operation: Operation | "upload" | "discard"; payload?: Setup | Selection; version: number; title: string; description: string };
const stepFor = (analysis: Analysis): Step => ["running", "result", "failed"].includes(analysis.state) ? "result" : analysis.data ? "comparison" : "data";

export function LabWorkspace({ analysis, brandId, workspace, canEdit, readToken }: { analysis: Analysis; brandId: string; workspace: string; canEdit: boolean; readToken: string }) {
  const router = useRouter(), [saved, setSaved] = useState<Analysis | null>(null);
  const current = saved && Number(saved.version) > Number(analysis.version) ? saved : analysis;
  const [step, setStep] = useState<Step>(stepFor(analysis)), [state, setState] = useState<LabState>({ status: "idle" });
  const [setupVersion, setSetupVersion] = useState(Number(analysis.version)), [setupDirty, setSetupDirty] = useState(false), [selectionDirty, setSelectionDirty] = useState(false), [formKey, setFormKey] = useState(0);
  const [file, setFile] = useState<File | null>(null), [confirmation, setConfirmation] = useState<Command | null>(null), [failedRead, setFailedRead] = useState<string | null>(null);
  const [pending, start] = useTransition(), busy = useRef(false), alive = useRef(true), cancelRef = useRef<HTMLButtonElement>(null), notice = useRef<HTMLDivElement>(null), uploadRef = useRef<HTMLInputElement>(null);
  const running = current.state === "running", blocked = state.conflict === true || running || state.signIn === true;
  if (setupVersion !== Number(current.version) && !setupDirty && !pending) setSetupVersion(Number(current.version));
  const setupStale = setupVersion !== Number(current.version);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { if (state.status !== "idle") notice.current?.focus(); }, [state]);
  const links = <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/${workspace}/settings/connections`}>Manage data connections</Link>;

  function accept(result: LabState, operation: Command["operation"]) {
    if (result.analysis && (result.analysis.id !== current.id || Number(result.analysis.version) <= Number(current.version))) result = { status: "error", conflict: true, message: "The response did not match this analysis version. Refresh and review before continuing." };
    setState(result);
    if (result.conflict) setFailedRead(readToken);
    if (result.analysis) {
      setSaved(result.analysis); setSetupDirty(false); setSelectionDirty(false); setSetupVersion(Number(result.analysis.version)); setFormKey(key => key + 1);
      if (operation === "setup") setStep(result.analysis.data ? "comparison" : "data");
      if (operation === "load" || operation === "selection") setStep("comparison");
      if (operation === "run") setStep("result");
      if (operation === "memory") setState({ ...result, message: "Result saved to Brand Memory." });
      if (operation === "upload") { setFile(null); if (uploadRef.current) uploadRef.current.value = ""; }
      router.refresh();
    }
  }

  function execute(command: Command) {
    if (busy.current || !canEdit || (blocked && command.operation !== "discard") || running || command.version !== Number(current.version)) return;
    if (command.operation === "discard") { setSetupDirty(false); setSelectionDirty(false); setSetupVersion(Number(current.version)); setFormKey(key => key + 1); setState({ status: "idle" }); setFailedRead(null); setConfirmation(null); return; }
    const operation = command.operation;
    busy.current = true; setConfirmation(null); setState({ status: "idle" });
    start(async () => {
      let result: LabState;
      try {
        if (operation === "upload") {
          if (!file || file.size === 0 || file.size > 10 * 1024 * 1024) { result = { status: "error", message: "Choose a non-empty CSV file up to 10 MB." }; }
          else {
            const data = new FormData(); data.set("file", file);
            const response = await fetch(`/api/brands/${brandId}/impact-lab/analyses/${current.id}/file?version=${command.version}`, { method: "POST", headers: { [REQUEST_HEADER]: "1" }, body: data, cache: "no-store", redirect: "error" });
            if (response.status === 401) result = { status: "error", signIn: true, message: "Your session expired. Sign in again before uploading." };
            else if (response.status >= 500) result = { status: "error", conflict: true, message: "The upload could not be confirmed. Refresh and review the saved analysis before retrying." };
            else {
              const body = await response.json();
              if (!response.ok) result = { ...problemToFormState(body, response.status), conflict: response.status === 409 };
              else if (!validId(body.id) || !validVersion(body.version)) result = { status: "error", conflict: true, message: "The upload response could not be confirmed. Refresh before continuing." };
              else result = { status: "saved", analysis: body, message: "CSV uploaded. Load the data to see its coverage and available series." };
            }
          }
        } else result = await changeAnalysis(brandId, current.id, command.version, operation, command.payload);
      } catch { result = { status: "error", conflict: true, message: "The connection was interrupted. Your draft is still here. Refresh and review the saved analysis before retrying." }; }
      finally { busy.current = false; }
      if (alive.current) accept(result, command.operation);
    });
  }

  function request(operation: Command["operation"], payload?: Setup | Selection) {
    if (blocked && operation !== "discard" || running || pending || !canEdit) return;
    if (operation === "setup" && payload && "name" in payload) {
      const errors = setupErrors(payload);
      if (Object.keys(errors).length) { setState({ status: "error", fieldErrors: errors }); return; }
    }
    const command: Command = { operation, payload, version: operation === "setup" ? setupVersion : Number(payload?.version ?? current.version), title: "", description: "" };
    if (operation === "discard") { command.title = "Discard your draft changes?"; command.description = "Your setup and selection drafts will be replaced with the latest saved analysis."; }
    if (operation === "setup" && current.data && payload && "source" in payload && (payload.start !== current.start || payload.end !== current.end || JSON.stringify(payload.source) !== JSON.stringify(setupOf(current).source))) { command.title = `Replace data for ${current.name}?`; command.description = "Changing the dates or source clears the loaded data, selections and current result. A previously saved Brand Memory lesson stays recorded."; }
    if ((operation === "load" || operation === "upload") && current.data) { command.title = `Reload data for ${current.name}?`; command.description = "This clears the loaded data, selections, checks and current result. Review the new data before running again."; }
    if ((operation === "selection" || operation === "run") && current.result) { command.title = `Replace the result for ${current.name}?`; command.description = "This clears the current result. A previously saved Brand Memory lesson stays recorded. Run the analysis again to produce a new result."; }
    if (command.title) setConfirmation(command); else execute(command);
  }

  return <div className="min-w-0 space-y-6">
    <div className="flex flex-wrap items-center justify-between gap-3"><div className="flex flex-wrap gap-2"><Badge variant={running ? "info" : current.state === "failed" ? "destructive" : "neutral"}>{stateNames[current.state] ?? current.state}</Badge><span className="self-center text-small text-muted-foreground">Version {current.version}</span></div><Button className={actionClass} variant="secondary" onClick={() => router.refresh()}>Refresh analysis</Button></div>
    <nav aria-label="Analysis steps"><ol className="grid gap-2" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 8rem), 1fr))" }}>{(["setup", "data", "comparison", "result"] as const).map((key, index) => <li key={key}><button className={`min-h-11 w-full min-w-0 rounded-md border px-3 py-2 text-left text-small wrap-anywhere transition-colors disabled:opacity-55 ${step === key ? "border-primary bg-primary-soft font-semibold text-primary-strong" : "border-border bg-card hover:bg-accent"}`} aria-current={step === key ? "step" : undefined} disabled={pending || (setupDirty || selectionDirty) && step !== key || key === "comparison" && !current.data || key === "result" && !["running", "result", "failed"].includes(current.state)} onClick={() => setStep(key)}>{index + 1}. {{ setup: "Setup", data: "Data", comparison: "Comparison", result: "Result" }[key]}</button></li>)}</ol></nav>
    {(setupDirty || selectionDirty) && <p className="text-small text-muted-foreground">Save, check or discard your draft changes before moving to another step.</p>}{!canEdit && <Alert variant="neutral">You can read this analysis. Only marketers and admins can change its setup, load data, run it or save a lesson.</Alert>}
    {state.status !== "idle" && <div ref={notice} tabIndex={-1} role={state.status === "error" ? "alert" : "status"} className="space-y-2 rounded-lg border border-border bg-card p-4 text-small wrap-anywhere"><p>{state.message ?? (state.fieldErrors ? "Check the fields below." : "Analysis updated.")}</p>{state.fieldErrors && <ul className="list-inside list-disc">{Object.entries(state.fieldErrors).map(([key, errors]) => <li key={key}>{label(key)}: {errors.join(" ")}</li>)}</ul>}{state.signIn && <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/sign-in?returnUrl=${encodeURIComponent(`/${workspace}/impact-lab/${current.id}`)}`} target="_blank" rel="noopener">Sign in again in a new tab</Link>}{state.signIn && <Button className={actionClass} variant="secondary" onClick={() => setState({ status: "idle" })}>Continue after signing in</Button>}</div>}
    {(state.conflict || setupStale && setupDirty) && <Alert variant="warning"><p>The saved analysis needs review. Your draft is still here. Refresh to fetch the latest version, then replace your draft when you are ready.</p><Button className={`${actionClass} mt-2 w-fit`} variant="secondary" disabled={pending || running || state.conflict && failedRead === readToken} onClick={() => request("discard")}>Use latest saved version</Button></Alert>}
    {step === "setup" && <section className="space-y-5 rounded-lg border border-border bg-card p-4 md:p-6">{canEdit && !running ? <><SetupForm key={current.id} initial={setupOf(current)} resetKey={setupVersion * 100000 + formKey} pending={pending} blocked={blocked || setupStale} state={state} onSave={draft => request("setup", draft)} onDirty={setSetupDirty} />{setupDirty && <Button className={actionClass} variant="secondary" disabled={pending || blocked} onClick={() => request("discard")}>Discard setup changes</Button>}</> : <><h2 className="font-semibold">Saved setup</h2><p className="whitespace-pre-wrap wrap-anywhere">{current.whatChanged}</p><p className="text-small">{date(current.start)} to {date(current.end)} · Affects {current.affectsKind}</p><p className="text-small">{sourceNames[current.source.kind]} · {label(current.source.metric)} · {grainNames[current.source.grain]}</p>{current.source.table && <p className="text-small wrap-anywhere">{current.source.table}</p>}</>}</section>}
    {step === "data" && <section className="min-w-0 space-y-6 rounded-lg border border-border bg-card p-4 md:p-6"><div className="space-y-2"><h2 className="font-semibold">Load outcome data</h2><p className="text-small text-muted-foreground">{sourceNames[current.source.kind]} · {label(current.source.metric)} · {grainNames[current.source.grain] ?? current.source.grain}</p><p className="text-small text-muted-foreground">Kairo loads up to a year before the change and the available change period. Review the actual coverage below before running.</p>{current.source.table && <p className="text-small wrap-anywhere">Table: {current.source.table}</p>}{current.source.kind !== "file" && links}</div>
      {current.source.kind === "file" && <div className="space-y-3"><p className="text-small text-muted-foreground">Upload a UTF-8 CSV, up to 10 MB, with exactly date, series and value columns. Use YYYY-MM-DD dates and non-negative numbers. Each date/series pair must be unique. Include unaffected comparison series and at least eight weeks before the change where possible.</p><Button className={actionClass} variant="secondary" onClick={() => { try { downloadFile(sampleCsv, "kairo-impact-lab-sample.csv"); setState({ status: "saved", message: "Sample CSV download started. Replace its illustrative dates and values with your own data." }); } catch { setState({ status: "error", message: "The sample download could not start. Try again." }); } }}>Download sample CSV</Button>{current.source.fileName && <p className="text-small wrap-anywhere">Uploaded file: {current.source.fileName}</p>}{canEdit && !running && <div className="space-y-3"><label htmlFor="lab-file" className="block text-small font-medium">Outcome CSV file</label><input ref={uploadRef} id="lab-file" className={`${control} h-auto py-2 text-small file:mr-3 file:rounded file:border-0 file:bg-muted file:p-2 file:text-foreground`} type="file" accept=".csv,text/csv" disabled={pending || blocked} onChange={event => setFile(event.target.files?.[0] ?? null)} /><Button className={actionClass} variant="secondary" disabled={pending || blocked || !file} onClick={() => request("upload")}>Upload CSV</Button></div>}</div>}
      {current.data ? <div className="space-y-3 rounded-md bg-muted p-4"><h3 className="font-semibold">Loaded data coverage</h3><dl className="grid gap-4 text-small sm:grid-cols-2">{[["Date range", `${date(current.data.from)} to ${date(current.data.to)}`], ["History before change", `${current.data.historyDays} days`], ["Change period covered", `${current.data.changeDays} of ${current.data.plannedDays} days`], ["Available series", String(current.data.series.length)], ["Currency", current.data.currency ?? "Not supplied; use the named outcome unit"]].map(([key, value]) => <div key={key}><dt className="text-muted-foreground">{key}</dt><dd className="mt-1 tabular-nums">{value}</dd></div>)}</dl></div> : <Alert variant="neutral">Data has not been loaded for this setup yet.</Alert>}
      <div className="flex flex-wrap gap-3">{canEdit && !running && <Button className={actionClass} variant={current.data ? "secondary" : "default"} pending={pending} disabled={blocked || current.source.kind === "file" && !current.source.fileName} onClick={() => request("load")}>{current.data ? "Reload data" : "Load data"}</Button>}{current.data && <Button className={actionClass} disabled={pending} onClick={() => setStep("comparison")}>Review comparison</Button>}</div>
    </section>}
    {step === "comparison" && <ComparisonPicker key={`${current.id}-${formKey}`} analysis={current} pending={pending} blocked={blocked || !canEdit} onCheck={selection => request("selection", selection)} onRun={() => request("run")} onDiscard={() => request("discard")} onDirty={setSelectionDirty} /> }
    {step === "result" && (running ? <Alert variant="info"><h2 className="font-semibold">Analysis running</h2><p>Kairo is estimating the effect and writing its explanation. This page updates when the result is ready. You can leave and return using this analysis link.</p></Alert> : current.state === "failed" ? <Alert variant="destructive"><h2 className="font-semibold">The analysis could not finish</h2><p>{current.problem ? label(current.problem) : "Review the data and comparison, then try again."}</p>{canEdit && <Button className={`${actionClass} mt-3 w-fit`} variant="secondary" pending={pending} disabled={blocked} onClick={() => request("run")}>Retry analysis</Button>}</Alert> : <AnalysisResult key={`${current.id}-${current.result?.finishedAt ?? "none"}`} analysis={current} workspace={workspace} canEdit={canEdit} pending={pending} blocked={blocked} onRemember={() => request("memory")} />)}
    <AlertDialog open={!!confirmation} onOpenChange={open => { if (!open) setConfirmation(null); }}><AlertDialogContent initialFocus={cancelRef}><AlertDialogHeader><AlertDialogTitle>{confirmation?.title}</AlertDialogTitle><AlertDialogDescription>{confirmation?.description}</AlertDialogDescription></AlertDialogHeader>{confirmation && confirmation.version !== Number(current.version) && <Alert variant="warning">The analysis changed. Cancel and review the latest version before continuing.</Alert>}<AlertDialogFooter><AlertDialogCancel ref={cancelRef}>Cancel</AlertDialogCancel><Button className={actionClass} variant="destructive-solid" disabled={blocked && confirmation?.operation !== "discard" || !confirmation || confirmation.version !== Number(current.version)} onClick={() => confirmation && execute(confirmation)}>Confirm {confirmation?.operation === "discard" ? "discard" : "replacement"}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </div>;
}
