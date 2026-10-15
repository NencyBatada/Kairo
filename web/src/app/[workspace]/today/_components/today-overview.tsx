import { Activity, Bell, ClipboardCheck, TrendingUp } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { NotificationLink } from "@/components/notification-link";
import { PageHeader } from "@/components/page-header";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { components } from "@/lib/core-api/schema";

import { markNotificationRead } from "../../settings/notifications/actions";
import { TodayAgentJobs } from "./today-agent-jobs";

type NotificationItem = components["schemas"]["NotificationItem"];

const futureSections = [
  {
    title: "Approvals",
    description: "Requests needing your decision will appear here when campaign approvals are available.",
    icon: ClipboardCheck,
  },
  {
    title: "Top opportunities",
    description: "Rising demand for your products will appear here when Opportunity Radar is available.",
    icon: TrendingUp,
  },
  {
    title: "Live tests",
    description: "Campaign results and pacing will appear here when campaigns are live.",
    icon: Activity,
  },
] as const;

type TodayOverviewProps = {
  items: NotificationItem[];
  workspace: string;
  brandId: string;
  timeZone: string;
};

export function TodayOverview({ items, workspace, brandId, timeZone }: TodayOverviewProps) {
  const format = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "short", timeZone });

  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Today" description="What needs you this morning." />
      <TodayAgentJobs />
      <section aria-labelledby="recent-updates-title" className="flex flex-col gap-3">
        <h2 id="recent-updates-title" className="font-display text-display-sm text-foreground">
          Recent updates
        </h2>
        {items.length === 0 ? (
          <EmptyState
            icon={Bell}
            as="h2"
            title="No recent updates"
            description="Updates about your workspace will appear here when there is something to share."
            className="py-10 md:py-12"
          />
        ) : (
          <Card className="gap-0 py-0">
            <ul className="divide-y divide-border">
              {items.map((item) => (
                <li key={item.id}>
                  <NotificationLink
                    href={`/${workspace}${item.link}`}
                    unread={item.readAt === null}
                    markRead={markNotificationRead.bind(null, brandId, Number(item.id))}
                    className="flex flex-col gap-2 px-4 py-4 transition-colors hover:bg-accent md:px-5 pointer-coarse:py-5"
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="min-w-0 font-semibold wrap-anywhere text-foreground">{item.title}</span>
                      {item.readAt === null && <Badge variant="info">New</Badge>}
                    </div>
                    <p className="max-w-prose text-small wrap-anywhere text-muted-foreground">{item.body}</p>
                    <time dateTime={item.createdAt} className="text-xs text-subtle-foreground">
                      {format.format(new Date(item.createdAt))}
                    </time>
                  </NotificationLink>
                </li>
              ))}
            </ul>
          </Card>
        )}
      </section>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {futureSections.map(({ title, description, icon: Icon }) => (
          <Card key={title} className="min-w-0 gap-2">
            <CardHeader className="flex-row items-center gap-2">
              <Icon className="size-4 text-primary" aria-hidden="true" />
              <CardTitle>{title}</CardTitle>
            </CardHeader>
            <CardContent>
              <CardDescription>{description}</CardDescription>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
