import { notFound } from "next/navigation";

import { NavMenu } from "@/components/nav-menu";
import { UserMenu } from "@/components/user-menu";
import { WorkspaceSwitcher } from "@/components/workspace-switcher";
import { getMe, getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";

export async function WorkspaceSidebar({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const [workspaces, me, invitations] = await Promise.all([getMyWorkspaces(), getMe(), getMyInvitations()]);
  const current = workspaces.find((candidate) => candidate.handle === workspace);
  if (!current) {
    notFound();
  }

  return (
    <div className="space-y-6">
      <WorkspaceSwitcher current={current} workspaces={workspaces} pendingInvitations={invitations.length} />
      <NavMenu workspace={current.handle} />
      <UserMenu name={me.name} email={me.email} />
    </div>
  );
}
