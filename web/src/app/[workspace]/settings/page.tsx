import type { Metadata } from "next";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { timeZoneOptions } from "@/lib/time-zones";

import { updateWorkspace } from "./actions";
import { WorkspaceForm } from "./_components/workspace-form";

export const metadata: Metadata = { title: "Workspace settings" };

export default async function WorkspaceSettingsPage({ params }: PageProps<"/[workspace]/settings">) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const details = await unwrap(
    await (await coreApi()).GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }),
  );

  return (
    <WorkspaceForm
      action={updateWorkspace.bind(null, workspace.id)}
      workspace={details}
      timeZones={timeZoneOptions()}
      canEdit={workspace.roles.includes("admin")}
    />
  );
}
