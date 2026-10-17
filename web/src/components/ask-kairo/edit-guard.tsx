"use client";

import { createContext, useContext, useEffect, useId, type Dispatch, type SetStateAction } from "react";

export const AskEditGuard = createContext<Dispatch<SetStateAction<Record<string, boolean>>> | null>(null);

export function useAskEditGuard(active: boolean) {
  const register = useContext(AskEditGuard), id = useId();
  useEffect(() => {
    if (!register) return;
    register(current => current[id] === active ? current : { ...current, [id]: active });
    return () => { register(current => { const next = { ...current }; delete next[id]; return next; }); };
  }, [register, id, active]);
}
