import { CampaignRoomTabs } from "./campaign-room-tabs";

export async function RoomTabs({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <CampaignRoomTabs roomId={decodeRoomId(id)} />;
}

function decodeRoomId(id: string) {
  try {
    return decodeURIComponent(id);
  } catch {
    return id;
  }
}
