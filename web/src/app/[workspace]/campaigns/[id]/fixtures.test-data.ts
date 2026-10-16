import type { components } from "@/lib/core-api/schema";

export const run: components["schemas"]["CampaignRunView"] = {
  id: "run-1", stage: "brief_draft", agent: "plan", kind: "starter_brief", status: "failed", attempts: 3,
  queuedAt: "2026-10-15T03:30:00Z", startedAt: "2026-10-15T03:31:00Z", finishedAt: "2026-10-15T03:32:00Z",
  model: null, promptVersion: null, inputTokens: null, outputTokens: null, error: "The agent could not finish. Try again.",
};

export const room: components["schemas"]["CampaignDetail"] = {
  id: "room-1", title: "Waterproof trainers for the rains", stage: "brief_draft", status: "needs_attention", version: 1,
  owner: { id: "owner-1", name: "Priya Shah" }, opportunityId: "opportunity-1", openedAt: "2026-10-15T03:30:00Z", updatedAt: "2026-10-15T03:32:00Z",
  tracker: {
    stages: ["brief_draft", "creating", "asset_review", "ready_to_launch", "launching", "live", "results_ready", "closed"].map((stage, index) => ({ stage, label: ["Brief draft", "Creating", "Asset review", "Ready to launch", "Launching", "Live", "Results ready", "Closed"][index], progress: index === 0 ? "current" : "upcoming" })),
    current: "brief_draft", ownerKind: "person", owner: "Priya Shah", nextAction: "The Plan agent couldn't finish. Try it again.", enteredAt: "2026-10-15T03:30:00Z",
  },
  pendingApproval: null, lastSendBack: null, discardReason: null,
  evidence: { topic: "Waterproof trainers", regions: [{ name: "Maharashtra" }], products: [{ title: "Rain Runner", id: "product-1" }], reasons: [{ text: "Demand is rising with the rains.", sources: [{ title: "Weather report", url: "https://example.com/weather" }] }] },
  brief: null, runs: [run], actions: { requestApproval: null, approve: null, sendBackTo: [], discard: true, retryRun: "run-1", requestApprovalBlocker: null, continueWithoutVideo: false, approveBlocker: null, requestCheck: false },
};

export const activity: components["schemas"]["CampaignActivityPage"] = {
  entries: [{ id: "52", occurredAt: "2026-10-15T03:32:00Z", actorType: "agent", actorName: "Plan agent", action: "campaign.stage_failed", label: "Agent run failed", details: { comment: "Please try again." } }, { id: "51", occurredAt: "2026-10-15T03:30:00Z", actorType: "user", actorName: "Priya Shah", action: "campaign_room.opened", label: "Campaign room opened", details: null }],
  nextBefore: "51",
};
