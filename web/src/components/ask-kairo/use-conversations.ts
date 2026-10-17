"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useAskSessionRecovery, useAskUpdates, useLiveConnection } from "@/components/workspace-live-provider";
import type { LiveWorkspace } from "@/components/workspace-live-provider";
import * as api from "@/lib/ask-kairo/api";
import { applyEvent, mergeDetail, withMessage, type AskEvent, type Conversation, type Detail, type Proposal, type Question, type Target } from "@/lib/ask-kairo/model";

export function useConversations(workspace: LiveWorkspace | null, open: boolean) {
  const [history, setHistory] = useState<Conversation[]>([]), [detail, setDetail] = useState<Detail | null>(null);
  const [draft, setDraft] = useState(""), [error, setError] = useState<api.AskError | null>(null), [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(false), [sending, setSending] = useState(false), [deciding, setDeciding] = useState(false);
  const [retry, setRetry] = useState<{ conversationId: string; question: Question; reviewed: boolean } | null>(null);
  const brandId = workspace?.id, selectedId = detail?.conversation.id;
  const identity = workspace ? `${workspace.id}:${workspace.userId ?? ""}` : undefined;
  const [owner, setOwner] = useState(identity);
  const loadedBrand = useRef<string | null>(null), epoch = useRef(0), operation = useRef(false), decisionBusy = useRef(false), selecting = useRef(false), reads = useRef(0);
  const connection = useLiveConnection();
  useAskSessionRecovery(!!draft.trim() || sending || retry !== null);
  const report = (value: unknown) => setError(value instanceof api.AskError ? value : new api.AskError("The request was interrupted. Refresh the conversation and try again."));

  useEffect(() => {
    epoch.current += 1; reads.current += 1; operation.current = false; decisionBusy.current = false; selecting.current = false; loadedBrand.current = null;
    return () => { epoch.current += 1; reads.current += 1; };
  }, [identity]);
  if (owner !== identity) {
    setOwner(identity); setHistory([]); setDetail(null); setDraft(""); setError(null); setNotice(""); setRetry(null); setSending(false); setLoading(false); setDeciding(false);
  }

  const refresh = useCallback(async (manual = true) => {
    if (!brandId || selecting.current) return;
    const version = epoch.current, read = ++reads.current;
    try {
      const [items, current] = await Promise.all([api.history(brandId), selectedId ? api.detail(brandId, selectedId) : Promise.resolve(null)]);
      if (version !== epoch.current || read !== reads.current) return;
      setHistory(items); if (current) setDetail(previous => mergeDetail(previous, current));
      if (manual) {
        setError(null); setNotice("Saved conversation refreshed.");
        setRetry(previous => previous ? { ...previous, reviewed: true } : null);
      }
    } catch (value) { if (version === epoch.current && read === reads.current) report(value); }
  }, [brandId, selectedId]);

  useEffect(() => {
    if (!open || !brandId || loadedBrand.current === brandId) return;
    const version = epoch.current;
    loadedBrand.current = brandId; setLoading(true);
    api.history(brandId).then(items => { if (version === epoch.current) setHistory(items); }).catch(value => { if (version === epoch.current) { loadedBrand.current = null; report(value); } }).finally(() => { if (version === epoch.current) setLoading(false); });
  }, [open, brandId, identity]);

  useAskUpdates(useCallback((event: AskEvent | null) => {
    if (!brandId || !selectedId) return;
    if (event && event.conversationId !== selectedId) return;
    if (event) setDetail(previous => previous ? applyEvent(previous, event) : previous);
    if (event?.type === "ask.answer.segment") setNotice(`Kairo added part ${(event.index ?? 0) + 1} of the answer. Still answering.`);
    void refresh(false);
  }, [brandId, selectedId, refresh]));

  useEffect(() => {
    if (!open || !selectedId || connection !== "open") return;
    const timer = window.setTimeout(() => void refresh(false), 0);
    return () => window.clearTimeout(timer);
  }, [open, selectedId, connection, refresh]);

  async function select(id: string) {
    if (!brandId || operation.current || decisionBusy.current || selecting.current) return;
    selecting.current = true;
    const version = epoch.current, read = ++reads.current;
    setLoading(true); setError(null); setNotice("");
    try { const next = await api.detail(brandId, id); if (version === epoch.current && read === reads.current) { setDetail(next); setRetry(null); } }
    catch (value) { if (version === epoch.current && read === reads.current) report(value); }
    finally { if (version === epoch.current) { selecting.current = false; setLoading(false); } }
  }

  function startNew() {
    if (operation.current || decisionBusy.current || selecting.current) return;
    reads.current += 1; setDetail(null); setRetry(null); setError(null); setNotice("New conversation. Your draft is kept."); setLoading(false);
  }

  async function send(target: Target, useRetry = false) {
    if (!brandId || operation.current || decisionBusy.current || detail?.messages.some(message => message.status === "running") && !useRetry) return;
    if (useRetry && (!retry || !retry.reviewed)) return;
    if (!useRetry && (!draft.trim() || draft.length > 4000 || retry)) return;
    const question: Question = useRetry ? retry!.question : { requestId: crypto.randomUUID(), text: draft.trim(), context: target };
    operation.current = true; reads.current += 1; setSending(true); setError(null); setNotice("Kairo is reading the workspace evidence.");
    const version = epoch.current;
    let current = detail;
    try {
      if (!current) {
        const conversation = await api.create(brandId);
        if (version !== epoch.current) return;
        current = { conversation, messages: [], proposals: [] };
        setHistory(previous => [conversation, ...previous.filter(item => item.id !== conversation.id)].slice(0, 50));
      }
      const conversationId = useRetry ? retry!.conversationId : current.conversation.id;
      setDetail(previous => withMessage(previous ?? current!, { id: question.requestId, requestId: question.requestId, question: question.text, context: question.context, status: "running", segments: [], evidence: [], error: null, createdAt: new Date().toISOString(), model: null, promptVersion: null }));
      setRetry({ conversationId, question, reviewed: false });
      const message = await api.send(brandId, conversationId, question);
      if (version !== epoch.current) return;
      setDetail(previous => previous ? withMessage(previous, message) : previous); setRetry(null);
      setDraft(previous => previous.trim() === question.text ? "" : previous);
      setNotice(message.status === "failed" ? "The answer was interrupted. Saved evidence and partial text remain available." : message.status === "running" ? "The saved answer is still running. Live updates or Refresh saved will recover it." : "Kairo finished this answer.");
      try {
        const latest = await api.detail(brandId, conversationId);
        if (version === epoch.current) setDetail(previous => mergeDetail(previous, latest));
      } catch (value) { if (version === epoch.current) { report(value); setNotice("The answer is saved. Refresh to recover its latest evidence and previews."); } }
    } catch (value) {
      if (version === epoch.current) {
        reads.current += 1;
        setRetry(previous => previous ? { ...previous, reviewed: false } : null);
        if (value instanceof api.AskError && !value.uncertain && value.status >= 400) {
          setRetry(null); setDetail(previous => previous ? { ...previous, messages: previous.messages.filter(item => item.id !== question.requestId) } : previous);
        }
        report(value); setNotice("Your question is kept. Refresh to check whether it was saved before retrying.");
      }
    } finally { if (version === epoch.current) { operation.current = false; setSending(false); } }
  }

  async function decide(proposal: Proposal, decision: "confirm" | "reject") {
    if (!brandId || !selectedId || decisionBusy.current || operation.current) return false;
    decisionBusy.current = true; reads.current += 1; setDeciding(true); setError(null);
    const version = epoch.current;
    try {
      const latest = await api.proposal(brandId, selectedId, proposal.id);
      if (version !== epoch.current) return false;
      if (JSON.stringify(latest) !== JSON.stringify(proposal)) {
        setDetail(previous => previous ? { ...previous, proposals: previous.proposals.map(item => item.id === latest.id ? latest : item) } : previous);
        throw new api.AskError("This preview changed. Cancel and review the latest preview before deciding.", 409);
      }
      const result = await api.decide(brandId, selectedId, proposal.id, decision);
      if (version !== epoch.current) return false;
      setDetail(previous => previous ? { ...previous, proposals: previous.proposals.map(item => item.id === result.id ? result : item) } : previous);
      setNotice(decision === "confirm" ? "Change applied. Normal checks and approval still apply." : "Preview discarded.");
      return true;
    } catch (value) { if (version === epoch.current) { reads.current += 1; report(value); } return false; }
    finally { if (version === epoch.current) { decisionBusy.current = false; setDeciding(false); } }
  }

  return { history, detail, draft, setDraft, error, notice, loading, sending, deciding, retry, connection, select, startNew, send, refresh, decide };
}
export type ConversationController = ReturnType<typeof useConversations>;
