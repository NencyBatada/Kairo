import { Suspense } from "react";

import { SignInPanel } from "./_components/sign-in-panel";

export default function SignInPage({ searchParams }: PageProps<"/sign-in">) {
  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <section className="w-full max-w-sm space-y-6 text-center">
        <h1 className="text-2xl font-semibold">Sign in to Kairo</h1>
        <Suspense fallback={null}>
          <SignInPanel searchParams={searchParams} />
        </Suspense>
      </section>
    </main>
  );
}
