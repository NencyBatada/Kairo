"use client";

import { Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useWorkspaceAssistant } from "@/components/workspace-assistant";

export function AskAboutAsset({ id, campaignId, label }: { id: string; campaignId: string; label: string }) {
  const assistant = useWorkspaceAssistant();
  if (!assistant) return null;
  return <Button variant="secondary" className="h-auto min-h-11 w-fit max-w-full whitespace-normal" onClick={event => assistant.askAbout({ kind: "asset", id, campaignId }, label, event.currentTarget)}><Sparkles className="size-4 shrink-0" aria-hidden="true" />Ask about this asset</Button>;
}
