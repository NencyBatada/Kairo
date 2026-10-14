import { WorkspaceLiveUpdates } from "@/components/workspace-live-updates";
import { getWorkspace } from "@/lib/core-api/queries";

export async function WorkspaceLiveBridge({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const { id } = await getWorkspace(workspace);
  return <WorkspaceLiveUpdates brandId={id} />;
}
