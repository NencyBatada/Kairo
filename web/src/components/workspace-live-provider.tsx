"use client";

import { createContext, type ReactNode, useContext, useMemo, useReducer } from "react";

import { type AgentJobProgress, mergeAgentProgress } from "@/lib/agent-progress";

export type LiveConnection = "connecting" | "open" | "reconnecting";

type LiveState = {
  jobs: AgentJobProgress[];
  connection: LiveConnection;
};

type LiveAction =
  | { type: "progress"; progress: AgentJobProgress }
  | { type: "connection"; connection: LiveConnection }
  | { type: "clear" };

export type LiveDispatch = {
  reportProgress: (progress: AgentJobProgress) => void;
  setConnection: (connection: LiveConnection) => void;
  clearJobs: () => void;
};

const initialState: LiveState = { jobs: [], connection: "connecting" };

const noop = () => {};
const detachedDispatch: LiveDispatch = { reportProgress: noop, setConnection: noop, clearJobs: noop };

const LiveStateContext = createContext<LiveState>({ jobs: [], connection: "open" });
const LiveDispatchContext = createContext<LiveDispatch>(detachedDispatch);

function reduce(state: LiveState, action: LiveAction): LiveState {
  switch (action.type) {
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
  const actions = useMemo<LiveDispatch>(
    () => ({
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

export function useAgentJobs(subject?: { kind: string; id: string }) {
  const { jobs } = useContext(LiveStateContext);
  const kind = subject?.kind;
  const id = subject?.id;
  return useMemo(
    () => (kind === undefined ? jobs : jobs.filter((job) => job.subject?.kind === kind && job.subject.id === id)),
    [jobs, kind, id],
  );
}
