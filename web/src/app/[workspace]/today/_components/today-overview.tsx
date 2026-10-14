import { Activity, Bell, ClipboardCheck, TrendingUp } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { components } from "@/lib/core-api/schema";

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

export function TodayOverview({ items, workspace }: { items: NotificationItem[]; workspace: string }) {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Today" description="What needs you this morning." />
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
                  <Link
                    href={`/${workspace}${item.link}`}
                    className="flex flex-col gap-2 px-4 py-4 transition-colors hover:bg-accent md:px-5 pointer-coarse:py-5"
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-foreground">{item.title}</span>
                      {item.readAt === null && <Badge variant="info">New</Badge>}
                    </div>
                    <p className="max-w-prose text-small text-muted-foreground">{item.body}</p>
                    <time dateTime={item.createdAt} className="text-xs text-subtle-foreground">
                      {new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(
                        new Date(item.createdAt),
                      )}
                    </time>
                  </Link>
                </li>
              ))}
            </ul>
          </Card>
        )}
      </section>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {futureSections.map(({ title, description, icon: Icon }) => (
          <Card key={title} className="gap-2">
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
