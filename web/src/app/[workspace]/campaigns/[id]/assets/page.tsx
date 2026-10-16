import { coreApi, unwrap } from "@/lib/core-api/client";
import { getRoom } from "../_lib/room";
import { StudioWorkspace } from "./_components/studio-workspace";
import { campaignTypes } from "./_lib/model";

export default async function AssetsPage({ params, searchParams }: PageProps<"/[workspace]/campaigns/[id]/assets">) {
  const [{ workspace: handle, id }, query] = await Promise.all([params, searchParams]);
  const { brandId, timeZone, room } = await getRoom(handle, id);
  const language = typeof query.language === "string" ? query.language : "";
  const campaignType = typeof query.type === "string" && Object.hasOwn(campaignTypes, query.type) ? query.type : "";
  const api = await coreApi();
  const set = await api.GET("/api/brands/{brandId}/campaigns/{campaignId}/assets", { params: { path: { brandId, campaignId: id } } }).then(unwrap);
  const selected = set.languages.includes(language) ? language : "";
  const shown = selected ? { ...set, groups: set.groups.filter((group) => group.language === selected || group.language === "zxx") } : set;
  return <StudioWorkspace set={shown} brandId={brandId} campaignId={id} timeZone={timeZone} handle={handle} language={selected} campaignType={campaignType} runs={room.runs} />;
}
