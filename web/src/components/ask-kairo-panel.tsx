"use client";

import { ExternalLink, Sparkles } from "lucide-react";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { evidencePath, pageTarget, sourceUrl, targetLabels, targetPath, type Evidence, type Target } from "@/lib/ask-kairo/model";
import type { LiveWorkspace } from "@/components/workspace-live-provider";
import { ProposalPreview } from "./ask-kairo/proposal-preview";
import type { ConversationController } from "./ask-kairo/use-conversations";

export function AskKairoPanel({ area, workspace, controller, selected, editing }: { area: string; workspace: LiveWorkspace | null; controller: ConversationController; selected: { pathname: string; target: Target; label: string } | null; editing: boolean }) {
  const pathname = usePathname() ?? "";
  const [follow, setFollow] = useState(true), end = useRef<HTMLDivElement>(null);
  const active = selected?.pathname === pathname ? selected : null;
  const target = active?.target ?? pageTarget(pathname, workspace?.handle ?? "");
  const label = active?.label ?? targetLabels[target.kind];
  const busy = controller.sending || controller.deciding || controller.loading;
  const running = controller.detail?.messages.some(message => message.status === "running");
  const textToken = controller.detail?.messages.map(message => `${message.id}:${message.segments.length}:${message.status}`).join("|");
  useEffect(() => { if (follow) end.current?.scrollIntoView?.({ block: "nearest", behavior: "instant" }); }, [textToken, follow]);
  const lastCompleted = controller.detail?.messages.find(message => message.requestId === controller.retry?.question.requestId && message.status !== "running");
  if (!workspace) return <div className="min-h-0 flex-1 overflow-y-auto p-5"><Alert>Workspace access is loading. Reopen Ask Kairo if it remains unavailable.</Alert><p className="mt-3 text-small text-muted-foreground">{area}</p></div>;
  return <div className="flex min-h-0 flex-1 flex-col overflow-y-auto">
    <div className="grid shrink-0 gap-3 border-b border-border p-4">
      <p className="text-xs text-subtle-foreground">Private to you in {workspace.name}. Latest 50 conversations; recent 100 messages and 100 previews per conversation.</p>
      <div className="flex items-end gap-2"><div className="min-w-0 flex-1"><label htmlFor="ask-history" className="mb-1 block text-small font-medium">Conversation</label><select id="ask-history" className="min-h-11 w-full min-w-0 rounded-md border border-input bg-card px-2 text-sm" value={controller.detail?.conversation.id ?? ""} disabled={busy} onChange={event => { if (event.target.value) void controller.select(event.target.value); else controller.startNew(); }}><option value="">New conversation</option>{controller.history.map(item => <option key={item.id} value={item.id}>{item.title}</option>)}</select></div><Button variant="secondary" disabled={busy} onClick={controller.startNew}>New</Button></div>
      <p className="text-small wrap-anywhere"><span className="font-medium">Ask about: </span>{label}{target.id && <span className="block font-mono text-xs text-subtle-foreground">{target.id}</span>}<span className="mt-1 block text-xs text-subtle-foreground">{area}{target.kind === "workspace" ? " · This screen uses workspace context." : " · Uses saved data; unsaved page edits are not sent."}</span></p>
    </div>
    <div className="min-h-32 flex-1 overflow-y-auto p-4" onScroll={event => { const box = event.currentTarget; setFollow(box.scrollHeight - box.scrollTop - box.clientHeight < 80); }} aria-label="Conversation messages" role="region" tabIndex={0}>
      {controller.loading && <p role="status" className="text-small text-muted-foreground">Loading conversation…</p>}
      {!controller.detail?.messages.length && !controller.loading && <div className="grid gap-4 py-8"><Sparkles className="size-7 text-primary" aria-hidden="true" /><h3 className="font-display text-display-sm">A clearer next step.</h3><p className="text-small text-muted-foreground">Ask about the saved item, its evidence or a draft change. Kairo cannot approve, launch or raise spend.</p><div className="grid gap-2">{["Explain the evidence for this item.", "What should I review next?"].map(text => <Button key={text} variant="secondary" className="h-auto min-h-11 justify-start whitespace-normal text-left" onClick={() => controller.setDraft(text)}>{text}</Button>)}</div></div>}
      <div className="grid min-w-0 gap-6">{controller.detail?.messages.map(message => <article key={message.requestId} className="grid min-w-0 gap-3" aria-label="Question and answer">
        <div className="min-w-0 rounded-lg bg-muted p-3"><p className="text-xs font-semibold text-subtle-foreground">You · {targetLabels[message.context.kind]}</p><p className="mt-1 whitespace-pre-wrap text-small wrap-anywhere">{message.question}</p>{targetPath(message.context, workspace.handle) && <a href={targetPath(message.context, workspace.handle)!} target="_blank" rel="noopener noreferrer" className="mt-2 inline-flex min-h-11 items-center text-xs text-primary underline">View question context<span className="sr-only"> in a new tab</span></a>}</div>
        <div className="grid min-w-0 gap-3"><h3 className="flex items-center gap-1.5 text-small font-semibold"><Sparkles className="size-4 text-primary" aria-hidden="true" />Kairo</h3>
          {message.segments.map((segment, index) => <div key={index} className="grid min-w-0 gap-2"><p className="whitespace-pre-wrap text-small leading-relaxed wrap-anywhere">{segment.text}</p>{segment.evidenceIds.length ? <ul className="flex flex-wrap gap-2">{segment.evidenceIds.map(id => { const evidence = message.evidence.find(item => item.id === id); return <li key={id}>{evidence ? <EvidenceChip evidence={evidence} handle={workspace.handle} /> : <span className="text-xs text-muted-foreground">Source {id}: loading saved evidence</span>}</li>; })}</ul> : <p className="text-xs text-warning-strong">No retrieved source supports this passage.</p>}</div>)}
          {message.status === "running" && <p className="text-small text-muted-foreground">{message.segments.length ? "Kairo is still answering…" : "Reading evidence…"}</p>}
          {message.status === "completed" && !message.segments.length && <p className="text-small text-muted-foreground">No answer text was returned.</p>}
          {message.status === "failed" && <Alert variant="warning">{message.error ?? "Kairo could not finish. Partial text stays available; ask a new question to try again."}</Alert>}
          {controller.detail?.proposals.filter(proposal => proposal.messageId === message.id).map(proposal => <ProposalPreview key={proposal.id} proposal={proposal} conversationId={controller.detail!.conversation.id} workspace={workspace} controller={controller} editing={editing} />)}
        </div>
      </article>)}</div><div ref={end} />
    </div>
    <div className="grid shrink-0 gap-3 border-t border-border bg-card p-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
      {!follow && <Button variant="ghost" onClick={() => { setFollow(true); end.current?.scrollIntoView?.({ block: "nearest" }); }}>Jump to latest</Button>}
      {controller.connection !== "open" && <p className="text-xs text-warning-strong">Live updates are reconnecting. Refresh to recover saved answers and previews.</p>}
      {controller.error && <div role="alert" className="space-y-2 text-small wrap-anywhere text-destructive-strong"><p>{controller.error.message}</p>{controller.error.status === 401 && <a href={`/sign-in?returnUrl=${encodeURIComponent(pathname)}`} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-11 items-center underline">Sign in in a new tab</a>}</div>}
      <p role="status" aria-atomic="true" className="text-xs wrap-anywhere text-muted-foreground">{controller.notice}</p>
      <div className="flex flex-wrap gap-2"><Button variant="ghost" disabled={controller.loading || controller.deciding} className="min-h-11" onClick={() => void controller.refresh()}>Refresh saved</Button>{controller.retry && !controller.sending && <Button variant="secondary" disabled={!controller.retry.reviewed || controller.deciding} className="h-auto min-h-11 whitespace-normal" onClick={() => void controller.send(controller.retry!.question.context, true)}>{lastCompleted ? "Recover saved answer" : "Retry same question"}</Button>}</div>
      <form className="grid gap-2" onSubmit={event => { event.preventDefault(); setFollow(true); void controller.send(target); }}>
        <label htmlFor="ask-question" className="text-small font-medium">Your question</label><Textarea id="ask-question" rows={2} className="max-h-40 min-h-20" value={controller.draft} onChange={event => controller.setDraft(event.target.value)} aria-describedby="ask-question-help" aria-invalid={controller.draft.length > 4000} />
        <div className="flex items-center justify-between gap-2"><p id="ask-question-help" className="text-xs text-subtle-foreground">{controller.draft.length.toLocaleString("en-IN")} / 4,000 · Enter for a new line</p><Button type="submit" pending={controller.sending} disabled={busy || !!running || !!controller.retry || !controller.draft.trim() || controller.draft.length > 4000}>Send</Button></div>
      </form>
    </div>
  </div>;
}

function EvidenceChip({ evidence, handle }: { evidence: Evidence; handle: string }) {
  const path = evidencePath(evidence, handle), sources = evidence.sources?.map(sourceUrl).filter((url): url is string => !!url) ?? [];
  return <details className="max-w-full rounded-lg border border-border bg-card text-small"><summary className="min-h-11 cursor-pointer px-3 py-2 wrap-anywhere text-primary">{evidence.title}</summary><div className="grid min-w-0 gap-2 border-t border-border p-3">{path ? <a href={path} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-11 items-center gap-1 wrap-anywhere text-primary underline">Open saved evidence<ExternalLink className="size-3.5 shrink-0" aria-hidden="true" /><span className="sr-only"> in a new tab</span></a> : <p className="text-xs text-warning-strong">The workspace evidence link is unavailable.</p>}{sources.map(url => <a key={url} href={url} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-11 items-center gap-1 wrap-anywhere text-primary underline">{new URL(url).hostname}<ExternalLink className="size-3.5 shrink-0" aria-hidden="true" /><span className="sr-only"> in a new tab</span></a>)}</div></details>;
}
