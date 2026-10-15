import { CloudRain, CalendarDays, Package, Zap } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { components } from "@/lib/core-api/schema";

export function TriggerSummary({ trigger }: { trigger: components["schemas"]["TriggerView"] }) {
  const Icon = trigger.source === "weather" ? CloudRain : trigger.source === "festival" ? CalendarDays : trigger.source === "stock" ? Package : Zap;
  const label = trigger.source === "weather" ? "Weather trigger" : trigger.source === "festival" ? "Festival trigger" : trigger.source === "stock" ? "Stock trigger" : "Triggered";
  return <div className="flex flex-col items-start gap-2 rounded-md bg-info-soft p-3 text-small"><Badge variant="info"><Icon className="size-3" aria-hidden="true" />{label}</Badge><p className="font-medium wrap-anywhere">{trigger.summary}</p><p className="wrap-anywhere text-muted-foreground">{trigger.regions.map((region) => region.name).join(", ") || "All of India"}{trigger.ended ? " · Condition ended" : ""}</p></div>;
}
