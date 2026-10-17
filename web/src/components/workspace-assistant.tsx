"use client";

import { X } from "lucide-react";
import { createContext, Suspense, useContext, useEffect, useRef, useState, useSyncExternalStore, type Dispatch, type ReactNode, type SetStateAction } from "react";

import { AskKairoPanel } from "@/components/ask-kairo-panel";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { usePathname } from "next/navigation";
import { useLiveWorkspace } from "@/components/workspace-live-provider";
import { useConversations } from "@/components/ask-kairo/use-conversations";
import { AskEditGuard } from "@/components/ask-kairo/edit-guard";
import type { Target } from "@/lib/ask-kairo/model";

type AssistantContextValue = {
  open: boolean;
  setOpen: (open: boolean, trigger?: HTMLElement) => void;
  setArea: Dispatch<SetStateAction<string>>;
  askAbout: (target: Target, label: string, trigger?: HTMLElement) => void;
  clearTarget: () => void;
};

const AssistantContext = createContext<AssistantContextValue | null>(null);
const wideQuery = "(min-width: 90rem)";

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

function AssistantLocation({ onChange }: { onChange: (pathname: string) => void }) {
  const pathname = usePathname() ?? "";
  useEffect(() => onChange(pathname), [pathname, onChange]);
  return null;
}

export function WorkspaceAssistant({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const returnFocus = useRef<HTMLElement | null>(null);
  const [area, setArea] = useState("Workspace");
  const [selected, setSelected] = useState<{ pathname: string; target: Target; label: string } | null>(null);
  const [guards, setGuards] = useState<Record<string, boolean>>({});
  const [pathname, setPathname] = useState("");
  const identity = useLiveWorkspace();
  const workspace = identity && pathname.split("/")[1] === identity.handle ? identity : null;
  const controller = useConversations(workspace, open);
  const panel = <AskKairoPanel area={area} workspace={workspace} controller={controller} selected={selected} editing={Object.values(guards).some(Boolean)} />;
  const wide = useSyncExternalStore(subscribeToWide, getWideSnapshot, getServerWideSnapshot);

  function changeOpen(next: boolean, trigger?: HTMLElement) {
    if (next) returnFocus.current = trigger ?? (document.activeElement instanceof HTMLElement && document.activeElement !== document.body ? document.activeElement : document.getElementById("ask-kairo-trigger"));
    setOpen(next);
    if (!next) {
      window.setTimeout(() => (returnFocus.current?.isConnected ? returnFocus.current : document.getElementById("ask-kairo-trigger"))?.focus(), 0);
    }
  }

  return (
    <AssistantContext.Provider value={{ open, setOpen: changeOpen, setArea, askAbout: (target, label, trigger) => { setSelected({ pathname, target, label }); changeOpen(true, trigger); }, clearTarget: () => setSelected(null) }}>
      <AskEditGuard.Provider value={setGuards}>
      <Suspense fallback={null}><AssistantLocation onChange={setPathname} /></Suspense>
      <div className={open && wide ? "min-w-0 wide:grid wide:grid-cols-[minmax(0,1fr)_26.25rem]" : "min-w-0"}>
        <div className="min-w-0">{children}</div>
        {open && wide && (
          <aside aria-label="Ask Kairo" className="sticky top-0 flex h-dvh min-w-0 flex-col border-l border-border bg-card">
            <div className="flex shrink-0 items-center justify-between border-b border-border px-5 py-4">
              <h2 className="font-display text-display-sm text-foreground">Ask Kairo</h2>
              <Button variant="ghost" size="icon" aria-label="Close" onClick={() => changeOpen(false)}>
                <X className="size-4" aria-hidden="true" />
              </Button>
            </div>
            {panel}
          </aside>
        )}
      </div>
      <Sheet open={open && !wide} onOpenChange={next => changeOpen(next)}>
        <SheetContent side="right" style={{ width: "min(100vw, 420px)" }}>
          <div className="shrink-0 border-b border-border px-5 py-5 pr-14">
            <SheetTitle className="font-display text-display-sm">Ask Kairo</SheetTitle>
          </div>
          {!wide && panel}
        </SheetContent>
      </Sheet>
      </AskEditGuard.Provider>
    </AssistantContext.Provider>
  );
}
