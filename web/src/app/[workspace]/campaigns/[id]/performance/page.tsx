import Link from "next/link";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { getRoom } from "../_lib/room";
import { PacingSummary } from "./_components/pacing-summary";
import { StockLog } from "./_components/stock-log";
import { EarlyReading } from "./_components/early-reading";

export default async function PerformancePage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const [{ room, brandId, timeZone }, workspace] = await Promise.all([getRoom(handle, id), getWorkspace(handle)]);
  const api = await coreApi();
  const path = { brandId, campaignId: room.id };
  const [pacing, log, lift] = await Promise.all([
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/pacing", { params: { path } }).then(result => result.response.status === 404 ? null : unwrap(result)),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/stock-pauses", { params: { path } }).then(unwrap),
    api.GET("/api/brands/{brandId}/campaigns/{campaignId}/lift", { params: { path } }).then(unwrap),
  ]);
  return <div className="min-w-0 space-y-8"><PacingSummary pacing={pacing} /><EarlyReading lift={lift} timeZone={timeZone} /><Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/campaigns/${room.id}/recommendations`}>Review recommendations and test status</Link><StockLog log={log} brandId={brandId} campaignId={room.id} canUndo={workspace.roles.some(role => role === "marketer" || role === "admin")} admin={workspace.roles.includes("admin")} timeZone={timeZone} /></div>;
}
