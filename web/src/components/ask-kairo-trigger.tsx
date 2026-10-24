"use client";

import { Sparkles } from "lucide-react";
import { useEffect, useId } from "react";

import { Button } from "@/components/ui/button";
import { useWorkspaceAssistant } from "@/components/workspace-assistant";

export function AskKairoTrigger({ area }: { area: string }) {
  const id = useId();
  const assistant = useWorkspaceAssistant();
  const setArea = assistant?.setArea;

  useEffect(() => {
    setArea?.(area);
  }, [area, setArea]);

  if (!assistant) {
    return null;
  }

  return (
    <Button
      id={id}
      data-ask-kairo-trigger
      variant="secondary"
      aria-expanded={assistant.open}
      onClick={event => { assistant.clearTarget(); assistant.setOpen(!assistant.open, event.currentTarget); }}
    >
      <Sparkles className="size-4" aria-hidden="true" />
      Ask Kairo
    </Button>
  );
}
