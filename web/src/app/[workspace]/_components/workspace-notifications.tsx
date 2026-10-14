import { NotificationBell } from "@/components/notification-bell";
import { getUnreadNotificationCount, getWorkspace } from "@/lib/core-api/queries";

type WorkspaceNotificationsProps = {
  params: Promise<{ workspace: string }>;
  className?: string;
};

export async function WorkspaceNotifications({ params, className }: WorkspaceNotificationsProps) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const unreadCount = await getUnreadNotificationCount(workspace.id);

  return <NotificationBell href={`/${handle}/settings/notifications`} unreadCount={unreadCount} className={className} />;
}
