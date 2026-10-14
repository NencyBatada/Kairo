"use client";

import { X } from "lucide-react";
import { createContext, useContext, useState, useSyncExternalStore, type Dispatch, type ReactNode, type SetStateAction } from "react";

import { AskKairoPanel } from "@/components/ask-kairo-panel";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";

type AssistantContextValue = {
  open: boolean;
  setOpen: (open: boolean) => void;
  setArea: Dispatch<SetStateAction<string>>;
};

const AssistantContext = createContext<AssistantContextValue | null>(null);
const wideQuery = "(min-width: 1440px)";

function subscribeToWide(callback: () => void) {
  if (typeof window.matchMedia !== "function") {
    return () => {};
  }
  const media = window.matchMedia(wideQuery);
  media.addEventListener("change", callback);
  return () => media.removeEventListener("change", callback);
}

function getWideSnapshot() {
  return typeof window.matchMedia === "function" && window.matchMedia(wideQuery).matches;
}

function getServerWideSnapshot() {
  return false;
}

export function useWorkspaceAssistant() {
  return useContext(AssistantContext);
}

export function WorkspaceAssistant({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [area, setArea] = useState("Workspace");
  const wide = useSyncExternalStore(subscribeToWide, getWideSnapshot, getServerWideSnapshot);

  function changeOpen(next: boolean) {
    setOpen(next);
    if (!next) {
      window.setTimeout(() => document.getElementById("ask-kairo-trigger")?.focus(), 0);
    }
  }

  return (
    <AssistantContext.Provider value={{ open, setOpen: changeOpen, setArea }}>
      <div className={open && wide ? "min-w-0 wide:grid wide:grid-cols-[minmax(0,1fr)_26.25rem]" : "min-w-0"}>
        <div className="min-w-0">{children}</div>
        {open && wide && (
          <aside aria-label="Ask Kairo" className="sticky top-0 flex h-dvh min-w-0 flex-col border-l border-border bg-card">
            <div className="flex items-center justify-between border-b border-border px-5 py-4">
              <h2 className="font-display text-display-sm text-foreground">Ask Kairo</h2>
              <Button variant="ghost" size="icon" aria-label="Close" onClick={() => changeOpen(false)}>
                <X className="size-4" aria-hidden="true" />
              </Button>
            </div>
            <AskKairoPanel area={area} />
          </aside>
        )}
      </div>
      <Sheet open={open && !wide} onOpenChange={changeOpen}>
        <SheetContent side="right" style={{ width: "min(100vw, 420px)" }}>
          <div className="border-b border-border px-5 py-5 pr-14">
            <SheetTitle className="font-display text-display-sm">Ask Kairo</SheetTitle>
          </div>
          <AskKairoPanel area={area} />
        </SheetContent>
      </Sheet>
    </AssistantContext.Provider>
  );
}
