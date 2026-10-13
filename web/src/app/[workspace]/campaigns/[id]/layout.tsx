import { Suspense } from "react";

import { PageHeader } from "@/components/page-header";

import { RoomTabs } from "./_components/room-tabs";

export default function CampaignRoomLayout({ children, params }: LayoutProps<"/[workspace]/campaigns/[id]">) {
  return (
    <div className="flex flex-col gap-8">
      <div>
        <PageHeader eyebrow="Campaigns" title="Campaign room" description="Where this campaign stands, who acts next, and everything it has produced." />
        <Suspense fallback={<div className="mt-6 h-11 border-b border-border" />}>
          <RoomTabs params={params} />
        </Suspense>
      </div>
      {children}
    </div>
  );
}
