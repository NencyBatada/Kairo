import { roleLabels, type PendingInvitation } from "@/lib/core-api/types";

type PendingInvitationsProps = {
  invitations: PendingInvitation[];
  revoke: (invitationId: string) => Promise<void>;
};

export function PendingInvitations({ invitations, revoke }: PendingInvitationsProps) {
  if (invitations.length === 0) {
    return <p className="text-sm text-zinc-600 dark:text-zinc-400">No invitations are waiting.</p>;
  }

  return (
    <ul className="divide-y divide-black/10 dark:divide-white/15">
      {invitations.map((invitation) => (
        <li key={invitation.id} className="flex items-center justify-between gap-4 py-3">
          <div>
            <p className="font-medium">{invitation.email}</p>
            <p className="text-sm text-zinc-600 dark:text-zinc-400">
              {invitation.roles.map((role) => roleLabels[role]).join(", ")} · invited by {invitation.invitedBy}
            </p>
          </div>
          <form action={revoke.bind(null, invitation.id)}>
            <button type="submit" className="text-sm text-red-700 hover:underline dark:text-red-300">
              Revoke
            </button>
          </form>
        </li>
      ))}
    </ul>
  );
}
