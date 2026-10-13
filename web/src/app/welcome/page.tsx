import Link from "next/link";
import { Suspense } from "react";

import { getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";

import { acceptInvitation, createWorkspace, declineInvitation } from "./actions";
import { CreateWorkspaceForm } from "./_components/create-workspace-form";
import { InvitationList } from "./_components/invitation-list";

const currencies = ["INR", "USD", "EUR", "GBP", "AED", "SGD", "AUD", "CAD", "JPY"];

export default function WelcomePage() {
  return (
    <main className="mx-auto max-w-xl space-y-10 p-8">
      <h1 className="text-2xl font-semibold">Welcome to Kairo</h1>
      <Suspense fallback={null}>
        <YourWorkspaces />
      </Suspense>
      <section className="space-y-4">
        <h2 className="text-lg font-semibold">Create a workspace</h2>
        <CreateWorkspaceForm
          action={createWorkspace}
          timeZones={Intl.supportedValuesOf("timeZone")}
          currencies={currencies}
        />
      </section>
    </main>
  );
}

async function YourWorkspaces() {
  const [invitations, workspaces] = await Promise.all([getMyInvitations(), getMyWorkspaces()]);

  return (
    <>
      {invitations.length > 0 && (
        <section className="space-y-4">
          <h2 className="text-lg font-semibold">Invitations</h2>
          <InvitationList invitations={invitations} accept={acceptInvitation} decline={declineInvitation} />
        </section>
      )}
      {workspaces.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-lg font-semibold">Your workspaces</h2>
          <ul className="space-y-1">
            {workspaces.map((workspace) => (
              <li key={workspace.id}>
                <Link href={`/${workspace.handle}/today`} className="underline-offset-2 hover:underline">
                  {workspace.name}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
