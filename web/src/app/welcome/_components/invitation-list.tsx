import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { roleLabels, type MyInvitation } from "@/lib/core-api/types";

type InvitationListProps = {
  invitations: MyInvitation[];
  accept: (invitationId: string) => Promise<void>;
  decline: (invitationId: string) => Promise<void>;
};

export function InvitationList({ invitations, accept, decline }: InvitationListProps) {
  return (
    <Card className="gap-0 overflow-hidden py-0 md:gap-0 md:py-0">
      <ul className="divide-y divide-border">
        {invitations.map((invitation) => (
          <li key={invitation.id} className="flex flex-col gap-3 px-4 py-4 md:flex-row md:items-center md:justify-between md:px-5">
            <div className="min-w-0">
              <p className="truncate font-semibold text-foreground" title={invitation.workspaceName}>
                {invitation.workspaceName}
              </p>
              <p className="text-small text-subtle-foreground">
                {invitation.invitedBy} invited you as {invitation.roles.map((role) => roleLabels[role]).join(", ")}.
              </p>
            </div>
            <div className="flex gap-2">
              <form action={accept.bind(null, invitation.id)} className="flex-1 md:flex-none">
                <Button type="submit" variant="secondary" size="sm" className="w-full">
                  Accept
                </Button>
              </form>
              <form action={decline.bind(null, invitation.id)} className="flex-1 md:flex-none">
                <Button type="submit" variant="ghost" size="sm" className="w-full">
                  Decline
                </Button>
              </form>
            </div>
          </li>
        ))}
      </ul>
    </Card>
  );
}
