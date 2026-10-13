import { CampaignRoomTabs } from "./campaign-room-tabs";

export async function RoomTabs({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace, id } = await params;
  return <CampaignRoomTabs workspace={workspace} roomId={decodeRoomId(id)} />;
}

function decodeRoomId(id: string) {
  try {
    return decodeURIComponent(id);
  } catch {
    return id;
  }
}
