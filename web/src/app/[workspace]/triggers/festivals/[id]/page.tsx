import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { FestivalForm } from "../_components/festival-form";
import { RemoveFestival } from "../_components/remove-festival";
import { removeFestival, saveFestival } from "../actions";

export const metadata: Metadata = { title: "Edit calendar event" };
export default async function EditFestivalPage({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) return <div className="flex flex-col gap-8"><PageHeader title="Edit event" /><Alert>Only workspace admins can change events.</Alert></div>;
  const api = await coreApi();
  const path = { brandId: workspace.id };
  const [options, events] = await Promise.all([api.GET("/api/brands/{brandId}/triggers/options", { params: { path } }).then(unwrap), api.GET("/api/brands/{brandId}/triggers/festivals", { params: { path, query: { from: "0001-01-01" } } }).then(unwrap)]);
  const event = events.find((value) => value.id === id);
  if (!event) notFound();
  return <div className="flex flex-col gap-8"><PageHeader eyebrow="Festival calendar" title={`Edit ${event.name}`} actions={<Link href={`/${handle}/triggers/festivals`} className="text-small text-primary hover:underline">Festival calendar</Link>} /><FestivalForm event={event} regions={options.regions} handle={handle} action={saveFestival.bind(null, workspace.id, id)} /><section className="flex max-w-160 flex-col gap-3 border-t border-border pt-6" aria-labelledby="remove-event-title"><h2 id="remove-event-title" className="text-base font-semibold">Remove from calendar</h2><RemoveFestival name={event.name} inUse={Number(event.rules) > 0} action={removeFestival.bind(null, workspace.id, id, handle)} /></section></div>;
}
