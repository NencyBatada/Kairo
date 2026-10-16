import { coreApi, unwrap } from "@/lib/core-api/client";
import { getRoom } from "../_lib/room";
import { ResultsReport } from "./_components/results-report";

export default async function ResultsPage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const { room, brandId, timeZone } = await getRoom(handle, id);
  const api = await coreApi();
  const [impact, memory] = await Promise.all([
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/impact-report", { params: { path: { brandId, campaignId: room.id } } }).then(unwrap),
    api.GET("/api/brands/{brandId}/brand-memory", { params: { path: { brandId }, query: { state: "active" } } }).then(unwrap),
  ]);
  return <ResultsReport impact={impact} entries={memory.entries} handle={handle} timeZone={timeZone} />;
}
