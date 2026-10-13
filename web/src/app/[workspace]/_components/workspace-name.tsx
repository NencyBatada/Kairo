import { getWorkspace } from "@/lib/core-api/queries";

export async function WorkspaceName({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const { name } = await getWorkspace(workspace);

  return <span title={name}>{name}</span>;
}
