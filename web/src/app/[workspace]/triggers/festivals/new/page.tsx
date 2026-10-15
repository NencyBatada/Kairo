import type { Metadata } from "next";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { FestivalForm } from "../_components/festival-form";
import { saveFestival } from "../actions";

export const metadata: Metadata = { title: "Add calendar event" };
export default async function NewFestivalPage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) return <div className="flex flex-col gap-8"><PageHeader title="Add event" /><Alert>Only workspace admins can add events.</Alert></div>;
  const options = await (await coreApi()).GET("/api/brands/{brandId}/triggers/options", { params: { path: { brandId: workspace.id } } }).then(unwrap);
  return <div className="flex flex-col gap-8"><PageHeader eyebrow="Festival calendar" title="Add event" description="Set its dates and markets, then use it in a festival trigger rule." /><FestivalForm regions={options.regions} handle={handle} action={saveFestival.bind(null, workspace.id, null)} /></div>;
}
