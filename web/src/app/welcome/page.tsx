import { ArrowRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { PageHeader } from "@/components/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";
import { initials } from "@/lib/initials";
import { timeZoneOptions } from "@/lib/time-zones";

import { acceptInvitation, createWorkspace, declineInvitation } from "./actions";
import { CreateWorkspaceForm } from "./_components/create-workspace-form";
import { InvitationList } from "./_components/invitation-list";
import { WelcomeHeader } from "./_components/welcome-header";

const currencies = ["INR", "USD", "EUR", "GBP", "AED", "SGD", "AUD", "CAD", "JPY"];

export const metadata: Metadata = { title: "Welcome" };

export default function WelcomePage() {
  return (
    <div className="min-h-dvh bg-background">
      <WelcomeHeader />
      <main className="mx-auto flex w-full max-w-2xl flex-col gap-10 px-4 py-10 md:py-14">
        <PageHeader title="Welcome to Kairo" description="Join a workspace you've been invited to, or create one for your brand." />
        <Suspense fallback={<Skeleton className="h-24 w-full rounded-lg" />}>
          <YourWorkspaces />
        </Suspense>
        <Card>
          <CardHeader>
            <CardTitle>Create a workspace</CardTitle>
            <CardDescription>One workspace holds one brand: its team, approvals and campaigns.</CardDescription>
          </CardHeader>
          <CardContent>
            <CreateWorkspaceForm action={createWorkspace} timeZones={timeZoneOptions()} currencies={currencies} />
          </CardContent>
        </Card>
      </main>
    </div>
  );
}

async function YourWorkspaces() {
  const [invitations, workspaces] = await Promise.all([getMyInvitations(), getMyWorkspaces()]);

  return (
    <>
      {invitations.length > 0 && (
        <section className="flex flex-col gap-3">
          <h2 className="text-base font-semibold text-foreground">Invitations</h2>
          <InvitationList invitations={invitations} accept={acceptInvitation} decline={declineInvitation} />
        </section>
      )}
      {workspaces.length > 0 && (
        <section className="flex flex-col gap-3">
          <h2 className="text-base font-semibold text-foreground">Your workspaces</h2>
          <Card className="gap-0 overflow-hidden py-0 md:gap-0 md:py-0">
            <ul className="divide-y divide-border">
              {workspaces.map((workspace) => (
                <li key={workspace.id}>
                  <Link
                    href={`/${workspace.handle}/today`}
                    className="flex items-center gap-3 px-4 py-3.5 transition-colors duration-120 hover:bg-accent md:px-5"
                  >
                    <span aria-hidden="true" className="grid size-8 shrink-0 place-items-center rounded-md bg-primary-soft text-small font-bold text-primary-strong">
                      {initials(workspace.name).slice(0, 1)}
                    </span>
                    <span className="min-w-0 flex-1 truncate font-semibold text-foreground" title={workspace.name}>
                      {workspace.name}
                    </span>
                    <ArrowRight className="size-4 text-subtle-foreground" aria-hidden="true" />
                  </Link>
                </li>
              ))}
            </ul>
          </Card>
        </section>
      )}
    </>
  );
}
