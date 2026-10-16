import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { getRoom } from "../_lib/room";
import { RecommendationsWorkspace } from "./_components/recommendations-workspace";

export default async function RecommendationsPage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const [{ room, brandId, timeZone }, workspace] = await Promise.all([getRoom(handle, id), getWorkspace(handle)]);
  const api = await coreApi(), paramsBrand = { path: { brandId } };
  const [list, brand, settings, members] = await Promise.all([
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/recommendations", { params: { path: { brandId, campaignId: room.id } } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: paramsBrand }).then(unwrap),
    api.GET("/api/brands/{brandId}/approval-settings", { params: paramsBrand }).then(unwrap),
    api.GET("/api/brands/{brandId}/members", { params: paramsBrand }).then(unwrap),
  ]);
  return <RecommendationsWorkspace room={room} list={list} brandId={brandId} handle={handle} currency={brand.currency} timeZone={timeZone} settings={settings} members={members.filter(member => member.roles.includes("approver")).map(member => ({ id: member.userId, name: member.name }))} marketer={workspace.roles.includes("marketer")} canDismiss={workspace.roles.some(role => role === "marketer" || role === "approver")} />;
}
