import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { roleLabels, type PendingInvitation } from "@/lib/core-api/types";

type PendingInvitationsProps = {
  invitations: PendingInvitation[];
  revoke: (invitationId: string) => Promise<void>;
};

export function PendingInvitations({ invitations, revoke }: PendingInvitationsProps) {
  if (invitations.length === 0) {
    return <p className="text-small text-subtle-foreground">No invitations are waiting.</p>;
  }

  return (
    <ul className="divide-y divide-border">
      {invitations.map((invitation) => (
        <li key={invitation.id} className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0">
          <div className="min-w-0">
            <p className="truncate font-semibold text-foreground" title={invitation.email}>
              {invitation.email}
            </p>
            <p className="text-small wrap-anywhere text-subtle-foreground">
              {invitation.roles.map((role) => roleLabels[role]).join(", ")} · invited by {invitation.invitedBy}
            </p>
          </div>
          <ConfirmDialog
            trigger={
              <Button variant="destructive" size="sm">
                Revoke
              </Button>
            }
            title={`Revoke the invitation to ${invitation.email}?`}
            description="They won't be able to join with it. You can invite them again later."
            confirmLabel="Revoke"
            action={revoke.bind(null, invitation.id)}
          />
        </li>
      ))}
    </ul>
  );
}
