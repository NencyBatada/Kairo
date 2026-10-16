export const stageLabels: Record<string, string> = {
  brief_draft: "Brief draft", creating: "Creating", asset_review: "Asset review", ready_to_launch: "Ready to launch",
  launching: "Launching", live: "Live", results_ready: "Results ready", closed: "Closed", discarded: "Discarded",
};

export const statusLabels: Record<string, string> = {
  in_progress: "In progress", agent_working: "Agent working", waiting_approval: "Waiting for approval",
  needs_attention: "Needs attention", closed: "Closed", discarded: "Discarded",
};

export function stageSegment(stage: string) {
  return ({ brief_draft: "brief", creating: "assets", asset_review: "review", ready_to_launch: "launch", launching: "launch", live: "performance", results_ready: "results", closed: "results" } as Record<string, string>)[stage];
}

export function elapsed(enteredAt: string, now: number) {
  const minutes = Math.max(0, Math.floor((now - Date.parse(enteredAt)) / 60_000));
  if (!Number.isFinite(minutes)) return "Time unavailable";
  if (minutes < 1) return "Less than a minute";
  if (minutes < 60) return `${minutes} ${minutes === 1 ? "minute" : "minutes"}`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} ${hours === 1 ? "hour" : "hours"}, ${minutes % 60} min`;
  const days = Math.floor(hours / 24);
  return `${days} ${days === 1 ? "day" : "days"}, ${hours % 24} hr`;
}

export function dates(timeZone: string) {
  return new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });
}
