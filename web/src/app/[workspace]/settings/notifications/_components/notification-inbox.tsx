"use client";

import { Bell } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { NotificationLink } from "@/components/notification-link";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import type { NotificationPage } from "@/lib/core-api/types";
import { cn } from "@/lib/utils";

import { markAllNotificationsRead, markNotificationRead } from "../actions";

type NotificationInboxProps = {
  brandId: string;
  workspace: string;
  page: NotificationPage;
  timeZone: string;
  olderHref: (before: string) => string;
};

export function NotificationInbox({ brandId, workspace, page, timeZone, olderHref }: NotificationInboxProps) {
  const format = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "short", timeZone });
  const unreadCount = Number(page.unreadCount);

  if (page.items.length === 0) {
    return (
      <EmptyState
        icon={Bell}
        title="Nothing to act on"
        description="When something needs you, it will appear here with a link to the screen where you can act."
      />
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-small text-muted-foreground" role="status">
          {unreadCount === 0 ? "All caught up" : unreadCount === 1 ? "1 unread notification" : `${unreadCount} unread notifications`}
        </p>
        {unreadCount > 0 && (
          <form action={markAllNotificationsRead.bind(null, brandId)}>
            <Button type="submit" variant="secondary" size="sm">
              Mark all read
            </Button>
          </form>
        )}
      </div>
      <Card className="gap-0 py-0">
        <ul className="divide-y divide-border">
          {page.items.map((item) => {
            const href = `/${workspace}${item.link}`;
            const unread = item.readAt === null;

            return (
              <li key={String(item.id)}>
                <NotificationLink
                  href={href}
                  unread={unread}
                  markRead={() => markNotificationRead(brandId, Number(item.id))}
                  className="flex flex-col gap-2 px-4 py-4 transition-colors hover:bg-accent md:px-5 pointer-coarse:py-5"
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold text-foreground">{item.title}</span>
                    {unread && <Badge variant="info">New</Badge>}
                  </div>
                  <p className="max-w-prose text-small text-muted-foreground">{item.body}</p>
                  <time dateTime={item.createdAt} className="text-xs text-subtle-foreground">
                    {format.format(new Date(item.createdAt))}
                  </time>
                </NotificationLink>
              </li>
            );
          })}
        </ul>
      </Card>
      {page.nextBefore !== null && page.nextBefore !== undefined && (
        <Link href={olderHref(String(page.nextBefore))} className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "self-start")}>
          Older notifications
        </Link>
      )}
    </div>
  );
}
