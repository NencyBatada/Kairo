import { Bell } from "lucide-react";
import Link from "next/link";

import { cn } from "@/lib/utils";

type NotificationBellProps = {
  href: string;
  unreadCount: number;
  className?: string;
};

export function unreadLabel(count: number) {
  if (count === 0) {
    return "Notifications, none unread";
  }

  return count === 1 ? "Notifications, 1 unread" : `Notifications, ${count} unread`;
}

export function NotificationBell({ href, unreadCount, className }: NotificationBellProps) {
  const count = Math.max(0, Math.floor(unreadCount));

  return (
    <Link
      href={href}
      aria-label={unreadLabel(count)}
      title={unreadLabel(count)}
      className={cn(
        "relative inline-grid size-9 shrink-0 place-items-center rounded-md text-muted-foreground transition-colors duration-120 hover:bg-accent hover:text-foreground pointer-coarse:size-11",
        className,
      )}
    >
      <Bell className="size-5" aria-hidden="true" />
      {count > 0 && (
        <span
          aria-hidden="true"
          className="absolute top-0.5 right-0.5 min-w-4.5 rounded-full bg-primary px-1 text-center text-xs leading-4.5 font-semibold text-primary-foreground tabular-nums pointer-coarse:top-1.5 pointer-coarse:right-1.5"
        >
          {count > 99 ? "99+" : count}
        </span>
      )}
    </Link>
  );
}
