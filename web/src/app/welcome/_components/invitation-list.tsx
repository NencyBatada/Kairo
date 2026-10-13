import { roleLabels, type MyInvitation } from "@/lib/core-api/types";

type InvitationListProps = {
  invitations: MyInvitation[];
  accept: (invitationId: string) => Promise<void>;
  decline: (invitationId: string) => Promise<void>;
};

export function InvitationList({ invitations, accept, decline }: InvitationListProps) {
  return (
    <ul className="divide-y divide-black/10 dark:divide-white/15">
      {invitations.map((invitation) => (
        <li key={invitation.id} className="flex items-center justify-between gap-4 py-3">
          <div>
            <p className="font-medium">{invitation.workspaceName}</p>
            <p className="text-sm text-zinc-600 dark:text-zinc-400">
              {invitation.invitedBy} invited you as {invitation.roles.map((role) => roleLabels[role]).join(", ")}.
            </p>
          </div>
          <div className="flex gap-2">
            <form action={accept.bind(null, invitation.id)}>
              <button type="submit" className="rounded-md bg-black px-3 py-1 text-sm text-white dark:bg-white dark:text-black">
                Accept
              </button>
            </form>
            <form action={decline.bind(null, invitation.id)}>
              <button type="submit" className="rounded-md border px-3 py-1 text-sm">
                Decline
              </button>
            </form>
          </div>
        </li>
      ))}
    </ul>
  );
}
