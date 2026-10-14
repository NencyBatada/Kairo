import type { Metadata } from "next";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { ConnectionList } from "./_components/connection-list";

export const metadata: Metadata = { title: "Connections" };

export default async function ConnectionsPage({ params, searchParams }: PageProps<"/[workspace]/settings/connections">) {
  const { workspace: handle } = await params;
  const { error } = await searchParams;
  const workspace = await getWorkspace(handle);
  const connections = await unwrap(
    await (await coreApi()).GET("/api/brands/{brandId}/connections", { params: { path: { brandId: workspace.id } } }),
  );

  return (
    <ConnectionList
      connections={connections}
      canManage={workspace.roles.includes("admin")}
      brandId={workspace.id}
      callbackError={typeof error === "string" ? error : undefined}
    />
  );
}
