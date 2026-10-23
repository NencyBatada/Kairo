"use client";

import { Bell } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

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
  const count = Number.isFinite(unreadCount) ? Math.max(0, Math.floor(unreadCount)) : 0;
  const [previous, remember] = useState(count);
  const [announcement, announce] = useState("");
  if (previous !== count) {
    remember(count);
    announce(unreadLabel(count));
  }

  return (
    <span className={cn("inline-flex", className)}><Link
      href={href}
      aria-label={unreadLabel(count)}
      title={unreadLabel(count)}
      className={cn(
        "relative inline-grid size-9 shrink-0 place-items-center rounded-md text-muted-foreground transition-colors duration-120 hover:bg-accent hover:text-foreground pointer-coarse:size-11",
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
    </Link><span role="status" aria-atomic="true" className="sr-only">{announcement}</span></span>
  );
}
