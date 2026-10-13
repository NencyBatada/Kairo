import type { Metadata } from "next";
import { Suspense } from "react";

import { Logo } from "@/components/logo";
import { Skeleton } from "@/components/ui/skeleton";

import { SignalMotif } from "./_components/signal-motif";
import { SignInPanel } from "./_components/sign-in-panel";

export const metadata: Metadata = { title: "Sign in" };

export default function SignInPage({ searchParams }: PageProps<"/sign-in">) {
  return (
    <div className="grid min-h-dvh bg-background lg:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]">
      <div className="relative hidden flex-col overflow-hidden border-r border-border bg-sidebar p-10 lg:flex xl:p-14">
        <Logo />
        <div className="mt-auto max-w-xl">
          <p className="font-display text-display-xl text-balance text-foreground">From rising demand to proven lift.</p>
          <p className="mt-4 max-w-prose text-base text-muted-foreground">
            Kairo finds rising demand, drafts on-brand Google Ads campaigns and measures the extra sales they cause. People approve every
            step.
          </p>
        </div>
        <SignalMotif className="mt-12 w-full max-w-xl" />
      </div>
      <main className="flex flex-col px-4 py-8 md:px-8">
        <Logo className="lg:hidden" />
        <div className="mx-auto my-auto w-full max-w-sm py-12">
          <h1 className="font-display text-display-md text-foreground md:text-display-lg">Sign in</h1>
          <p className="mt-2 text-muted-foreground">Use your Google account. Kairo reads only your name, email address and profile picture.</p>
          <div className="mt-8">
            <Suspense fallback={<Skeleton className="h-10 w-full" />}>
              <SignInPanel searchParams={searchParams} />
            </Suspense>
          </div>
        </div>
      </main>
    </div>
  );
}
