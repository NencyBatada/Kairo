export const WORKSPACE_LIVE_EVENTS = [
  "workspace.changed",
  "team.changed",
  "invitations.changed",
  "approval_settings.changed",
  "check_settings.changed",
  "connection.changed",
  "feeds.changed",
  "brand_kit.changed",
  "brand_kit.files_changed",
  "brand_kit.drafting",
  "catalogue.imported",
  "catalogue.product_changed",
  "opportunities.refreshed",
  "opportunity.changed",
  "opportunity_topics.changed",
  "trigger_rules.changed",
  "festivals.changed",
  "campaign.changed",
  "campaigns.changed",
  "launch.changed",
  "launch.stock_changed",
  "launch.pacing_changed",
  "care.recommendations_changed",
  "notification.created",
  "notifications.read",
] as const;

export const AGENT_PROGRESS_EVENT = "agent.progress";

export const HANDLED_LIVE_EVENTS: readonly string[] = [...WORKSPACE_LIVE_EVENTS, AGENT_PROGRESS_EVENT];

export function liveStreamUrl(brandId: string) {
  return `/api/brands/${brandId}/live`;
}
