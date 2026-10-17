import { WorkspaceLiveUpdates } from "@/components/workspace-live-updates";
import { getMe, getWorkspace } from "@/lib/core-api/queries";
import { coreApi, unwrap } from "@/lib/core-api/client";

export async function WorkspaceLiveBridge({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const current = await getWorkspace(workspace);
  const [details, me] = await Promise.all([coreApi().then(api => api.GET("/api/brands/{brandId}", { params: { path: { brandId: current.id } } })).then(unwrap), getMe()]);
  return <WorkspaceLiveUpdates brandId={current.id} workspace={{ ...details, userId: me.id }} />;
}
