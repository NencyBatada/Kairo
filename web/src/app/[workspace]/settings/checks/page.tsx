import type { Metadata } from "next";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { CheckSettingsForm } from "./_components/check-settings-form";

export const metadata: Metadata = { title: "Brand Guardian settings" };

export default async function CheckSettingsPage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const [settings, brand] = await Promise.all([
    api.GET("/api/brands/{brandId}/check-settings", { params: { path: { brandId: workspace.id } } }).then(unwrap),
    api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }).then(unwrap),
  ]);
  return <CheckSettingsForm settings={settings} brandId={workspace.id} canEdit={workspace.roles.includes("admin")} timeZone={brand.timeZone} />;
}
