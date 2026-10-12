import { Suspense } from "react";

import { RoomTabs } from "./_components/room-tabs";

export default function CampaignRoomLayout({ children, params }: LayoutProps<"/campaigns/[id]">) {
  return (
    <div className="space-y-6">
      <Suspense fallback={null}>
        <RoomTabs params={params} />
      </Suspense>
      {children}
    </div>
  );
}
