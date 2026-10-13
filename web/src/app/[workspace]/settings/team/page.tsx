import { coreApi, unwrap } from "@/lib/core-api/client";
import { getMe, getWorkspace } from "@/lib/core-api/queries";

import { changeRoles, inviteMember, removeMember, revokeInvitation } from "../actions";
import { InviteForm } from "./_components/invite-form";
import { MemberRow } from "./_components/member-row";
import { PendingInvitations } from "./_components/pending-invitations";

export default async function TeamPage({ params }: PageProps<"/[workspace]/settings/team">) {
  const { workspace: handle } = await params;
  const [workspace, me] = await Promise.all([getWorkspace(handle), getMe()]);
  const api = await coreApi();
  const brandId = workspace.id;
  const isAdmin = workspace.roles.includes("admin");
  const members = await unwrap(await api.GET("/api/brands/{brandId}/members", { params: { path: { brandId } } }));
  const invitations = isAdmin
    ? await unwrap(await api.GET("/api/brands/{brandId}/invitations", { params: { path: { brandId } } }))
    : [];

  return (
    <div className="space-y-10">
      <section className="space-y-2">
        <h2 className="text-lg font-semibold">Members</h2>
        <ul className="divide-y divide-black/10 dark:divide-white/15">
          {members.map((member) => (
            <MemberRow
              key={member.userId}
              member={member}
              isSelf={member.userId === me.id}
              canManage={isAdmin}
              changeRoles={changeRoles.bind(null, brandId, member.userId)}
              remove={removeMember.bind(null, brandId, member.userId)}
            />
          ))}
        </ul>
      </section>
      {isAdmin && (
        <>
          <section className="space-y-4">
            <h2 className="text-lg font-semibold">Invite someone</h2>
            <InviteForm action={inviteMember.bind(null, brandId)} />
          </section>
          <section className="space-y-2">
            <h2 className="text-lg font-semibold">Waiting invitations</h2>
            <PendingInvitations invitations={invitations} revoke={revokeInvitation.bind(null, brandId)} />
          </section>
        </>
      )}
    </div>
  );
}
