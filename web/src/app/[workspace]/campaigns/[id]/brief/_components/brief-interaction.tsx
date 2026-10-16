"use client";

import { createContext } from "react";

export const BriefInteraction = createContext<{ blocked: boolean; setProofBusy: (busy: boolean) => void }>({ blocked: false, setProofBusy: () => {} });
