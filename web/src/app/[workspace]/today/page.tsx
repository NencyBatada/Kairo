import type { Metadata } from "next";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { TodayOverview } from "./_components/today-overview";

export const metadata: Metadata = { title: "Today" };

export default async function TodayPage({ params }: PageProps<"/[workspace]/today">) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const page = await unwrap(
    await api.GET("/api/brands/{brandId}/notifications", {
      params: { path: { brandId: workspace.id }, query: { limit: 5 } },
    }),
  );

  return <TodayOverview items={page.items} />;
}
