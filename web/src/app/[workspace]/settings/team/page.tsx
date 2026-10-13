import type { Metadata } from "next";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getMe, getWorkspace } from "@/lib/core-api/queries";

import { changeRoles, inviteMember, removeMember, revokeInvitation } from "../actions";
import { InviteForm } from "./_components/invite-form";
import { MemberRow } from "./_components/member-row";
import { PendingInvitations } from "./_components/pending-invitations";

export const metadata: Metadata = { title: "Team" };

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
    <div className="flex flex-col gap-10">
      <section aria-labelledby="members-heading" className="flex flex-col gap-3">
        <div>
          <h2 id="members-heading" className="text-base font-semibold text-foreground">
            Members
          </h2>
          <p className="text-small text-subtle-foreground">
            {members.length === 1 ? "1 person can open this workspace." : `${members.length} people can open this workspace.`}
          </p>
        </div>
        <Card className="gap-0 py-0 md:gap-0 md:py-0">
          <ul className="divide-y divide-border">
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
        </Card>
      </section>
      {isAdmin && (
        <div className="grid items-start gap-6 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Invite someone</CardTitle>
              <CardDescription>They sign in to Kairo with Google using this email.</CardDescription>
            </CardHeader>
            <CardContent>
              <InviteForm action={inviteMember.bind(null, brandId)} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Waiting invitations</CardTitle>
              <CardDescription>Invitations expire after 14 days.</CardDescription>
            </CardHeader>
            <CardContent>
              <PendingInvitations invitations={invitations} revoke={revokeInvitation.bind(null, brandId)} />
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
