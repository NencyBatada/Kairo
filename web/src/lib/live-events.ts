export const WORKSPACE_LIVE_EVENTS = [
  "workspace.changed",
  "team.changed",
  "invitations.changed",
  "approval_settings.changed",
  "connection.changed",
  "feeds.changed",
  "notification.created",
  "notifications.read",
] as const;

export function liveStreamUrl(brandId: string) {
  return `/api/brands/${brandId}/live`;
}
