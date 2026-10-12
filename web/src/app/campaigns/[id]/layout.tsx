import { Suspense } from "react";

import { CampaignRoomTabs } from "./_components/campaign-room-tabs";

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

async function RoomTabs({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <CampaignRoomTabs roomId={id} />;
}
