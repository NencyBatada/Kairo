"use client";

import { createContext, type ReactNode, useContext, useEffect, useMemo, useReducer, useRef } from "react";
import type { Workspace } from "@/lib/core-api/types";
import type { AskEvent } from "@/lib/ask-kairo/model";
export type LiveWorkspace = Workspace & { timeZone?: string; userId?: string };

import { type AgentJobProgress, mergeAgentProgress } from "@/lib/agent-progress";

export type LiveConnection = "connecting" | "open" | "reconnecting";

type LiveState = {
  workspace: LiveWorkspace | null;
  jobs: AgentJobProgress[];
  connection: LiveConnection;
};

type LiveAction =
  | { type: "workspace"; workspace: LiveWorkspace | null }
  | { type: "progress"; progress: AgentJobProgress }
  | { type: "connection"; connection: LiveConnection }
  | { type: "clear" };

export type LiveDispatch = {
  setAskDraftActive: (active: boolean) => void;
  hasAskDraft: () => boolean;
  setWorkspace: (workspace: LiveWorkspace | null) => void;
  publishAsk: (event: AskEvent | null) => void;
  subscribeAsk: (listener: (event: AskEvent | null) => void) => () => void;
  reportProgress: (progress: AgentJobProgress) => void;
  setConnection: (connection: LiveConnection) => void;
  clearJobs: () => void;
};

const initialState: LiveState = { workspace: null, jobs: [], connection: "connecting" };

const noop = () => {};
const detachedDispatch: LiveDispatch = { setAskDraftActive: noop, hasAskDraft: () => false, setWorkspace: noop, publishAsk: noop, subscribeAsk: () => noop, reportProgress: noop, setConnection: noop, clearJobs: noop };

const LiveStateContext = createContext<LiveState>({ workspace: null, jobs: [], connection: "open" });
const LiveDispatchContext = createContext<LiveDispatch>(detachedDispatch);

function reduce(state: LiveState, action: LiveAction): LiveState {
  switch (action.type) {
    case "workspace":
      return JSON.stringify(state.workspace) === JSON.stringify(action.workspace) ? state : { ...state, workspace: action.workspace };
    case "progress": {
      const jobs = mergeAgentProgress(state.jobs, action.progress);
      return jobs === state.jobs ? state : { ...state, jobs };
    }
    case "connection":
      return state.connection === action.connection ? state : { ...state, connection: action.connection };
    case "clear":
      return state.jobs.length === 0 ? state : { ...state, jobs: [] };
  }
}

export function WorkspaceLiveProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(reduce, initialState);
  const askListeners = useRef(new Set<(event: AskEvent | null) => void>());
  const askDraftActive = useRef(false);
  const actions = useMemo<LiveDispatch>(
    () => ({
      setAskDraftActive: active => { askDraftActive.current = active; },
      hasAskDraft: () => askDraftActive.current,
      setWorkspace: workspace => dispatch({ type: "workspace", workspace }),
      publishAsk: event => askListeners.current.forEach(listener => listener(event)),
      subscribeAsk: listener => { askListeners.current.add(listener); return () => { askListeners.current.delete(listener); }; },
      reportProgress: (progress) => dispatch({ type: "progress", progress }),
      setConnection: (connection) => dispatch({ type: "connection", connection }),
      clearJobs: () => dispatch({ type: "clear" }),
    }),
    [],
  );

  return (
    <LiveDispatchContext.Provider value={actions}>
      <LiveStateContext.Provider value={state}>{children}</LiveStateContext.Provider>
    </LiveDispatchContext.Provider>
  );
}

export function useLiveDispatch() {
  return useContext(LiveDispatchContext);
}

export function useLiveConnection() {
  return useContext(LiveStateContext).connection;
}

export function useLiveWorkspace() { return useContext(LiveStateContext).workspace; }

export function useAskUpdates(listener: (event: AskEvent | null) => void) {
  const { subscribeAsk } = useLiveDispatch();
  useEffect(() => subscribeAsk(listener), [subscribeAsk, listener]);
}

export function useAskSessionRecovery(active: boolean) {
  const { setAskDraftActive } = useLiveDispatch();
  useEffect(() => { setAskDraftActive(active); return () => setAskDraftActive(false); }, [active, setAskDraftActive]);
}

export function useAgentJobs(subject?: { kind: string; id: string }) {
  const { jobs } = useContext(LiveStateContext);
  const kind = subject?.kind;
  const id = subject?.id;
  return useMemo(
    () => (kind === undefined ? jobs : jobs.filter((job) => job.subject?.kind === kind && job.subject.id === id)),
    [jobs, kind, id],
  );
}
