import { notFound } from "next/navigation";

import { Logo } from "@/components/logo";
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
    <div className="flex min-h-full flex-col gap-5 px-3 pt-4 pb-3">
      <Logo href={`/${current.handle}/today`} className="self-start px-2 py-1" />
      <WorkspaceSwitcher current={current} workspaces={workspaces} pendingInvitations={invitations.length} />
      <NavMenu workspace={current.handle} />
      <div className="mt-auto border-t border-border pt-3">
        <UserMenu name={me.name} email={me.email} pictureUrl={me.pictureUrl} />
      </div>
    </div>
  );
}
