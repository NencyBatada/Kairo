"use client";

import { Sparkles } from "lucide-react";

import { Button } from "@/components/ui/button";
import { useWorkspaceAssistant } from "@/components/workspace-assistant";

export function AskKairoTrigger() {
  const assistant = useWorkspaceAssistant();
  if (!assistant) {
    return null;
  }

  return (
    <Button
      id="ask-kairo-trigger"
      variant="secondary"
      aria-expanded={assistant.open}
      onClick={() => assistant.setOpen(!assistant.open)}
    >
      <Sparkles className="size-4" aria-hidden="true" />
      Ask Kairo
    </Button>
  );
}
