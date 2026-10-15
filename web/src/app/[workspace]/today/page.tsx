import type { Metadata } from "next";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { TodayOverview } from "./_components/today-overview";

export const metadata: Metadata = { title: "Today" };

export default async function TodayPage({ params }: PageProps<"/[workspace]/today">) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const brandId = workspace.id;
  const [page, details, opportunities] = await Promise.all([
    unwrap(
      await api.GET("/api/brands/{brandId}/notifications", {
        params: { path: { brandId }, query: { limit: 5 } },
      }),
    ),
    unwrap(await api.GET("/api/brands/{brandId}", { params: { path: { brandId } } })),
    unwrap(await api.GET("/api/brands/{brandId}/opportunities", { params: { path: { brandId }, query: { status: "open", triggered: true } } })),
  ]);

  return <TodayOverview items={page.items} workspace={handle} brandId={brandId} timeZone={details.timeZone} triggered={opportunities.items.slice(0, 4)} />;
}
