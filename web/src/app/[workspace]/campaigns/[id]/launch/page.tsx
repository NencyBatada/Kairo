import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { getRoom } from "../_lib/room";
import { LaunchWorkspace } from "./_components/launch-workspace";

export default async function LaunchPage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const [{ room, brandId, timeZone }, workspace] = await Promise.all([getRoom(handle, id), getWorkspace(handle)]);
  const api = await coreApi();
  const options = { params: { path: { brandId, campaignId: room.id } } };
  const [view, progress, brief, proof, set] = await Promise.all([
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/launch-plan", options).then(unwrap),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/launch", options).then(result => result.response.status === 404 ? null : unwrap(result)),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/brief", options).then(unwrap),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/proof-plan", options).then(unwrap),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/assets", options).then(unwrap),
  ]);
  return <LaunchWorkspace room={room} view={view} launch={progress} brief={brief} proof={proof} set={set} brandId={brandId} handle={handle} timeZone={timeZone} marketer={workspace.roles.includes("marketer")} />;
}
