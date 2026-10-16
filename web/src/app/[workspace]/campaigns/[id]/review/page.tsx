import { coreApi, unwrap } from "@/lib/core-api/client";
import { RoomRuns } from "../_components/room-runs";
import { getRoom } from "../_lib/room";
import { retryRun } from "../actions";
import { ReviewWorkspace } from "./_components/review-workspace";

export default async function ReviewPage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const { room, brandId, timeZone } = await getRoom(handle, id);
  const set = await (await coreApi()).GET("/api/brands/{brandId}/campaigns/{campaignId}/assets", { params: { path: { brandId, campaignId: room.id } } }).then(unwrap);
  return <div className="space-y-8"><ReviewWorkspace room={room} set={set} brandId={brandId} handle={handle} timeZone={timeZone} /><RoomRuns key={`${room.id}:${room.actions.retryRun}`} roomId={room.id} runs={room.runs} retryId={room.actions.retryRun} retry={retryRun.bind(null, brandId, room.id, room.actions.retryRun ?? "")} timeZone={timeZone} /></div>;
}
