import type { Metadata } from "next";
import Link from "next/link";

import { PageHeader } from "@/components/page-header";

import { CampaignRoomTabs } from "./_components/campaign-room-tabs";
import { RoomTracker } from "./_components/room-tracker";
import { getRoom } from "./_lib/room";

export const metadata: Metadata = { title: "Campaign room" };

export default async function CampaignRoomLayout({ children, params }: LayoutProps<"/[workspace]/campaigns/[id]">) {
  const { workspace: handle, id } = await params;
  const { room, timeZone, now } = await getRoom(handle, id);
  return (
    <div className="flex flex-col gap-8">
      <div className="[&_h1]:wrap-anywhere">
        <PageHeader eyebrow="Campaign room" title={room.title} description="Where this campaign stands, who acts next, and everything it has produced." actions={<Link className="inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11" href={`/${handle}/campaigns`}>All campaigns</Link>} />
      </div>
      <RoomTracker room={room} handle={handle} timeZone={timeZone} now={now} />
      <CampaignRoomTabs workspace={handle} roomId={room.id} />
      {children}
    </div>
  );
}
