import { FileText } from "lucide-react";

import { EmptyState } from "@/components/empty-state";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { getRoom } from "../_lib/room";
import { BriefWorkspace } from "./_components/brief-workspace";
import { RegionsPlan } from "./_components/regions-plan";

export default async function BriefPage({ params }: PageProps<"/[workspace]/campaigns/[id]/brief">) {
  const { workspace: handle, id } = await params;
  const [{ room, brandId, timeZone, now }, workspace] = await Promise.all([getRoom(handle, id), getWorkspace(handle)]);
  const api = await coreApi();
  const path = { brandId, campaignId: id };
  const [detail, proof] = await Promise.all([
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/brief", { params: { path } }).then(unwrap),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/proof-plan", { params: { path } }).then(unwrap),
  ]);
  if (!detail.brief) return (
    <EmptyState
      icon={FileText}
      title="No brief yet"
      description="Kairo drafts the brief here, with the reasoning for each field, the budget against your limit, and the test and holdout regions."
    />
  );
  return <BriefWorkspace detail={detail} room={room} proof={proof} brandId={brandId} canEdit={workspace.roles.includes("marketer")} timeZone={timeZone} now={now}><RegionsPlan result={proof} brandId={brandId} campaignId={id} campaignVersion={String(room.version)} canEdit={workspace.roles.includes("marketer")} /></BriefWorkspace>;
}
