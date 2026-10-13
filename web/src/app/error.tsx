"use client";

import { ErrorState } from "@/components/error-state";
import { SiteHeader } from "@/components/site-header";

export default function RootError({ retry }: { retry: () => void }) {
  return (
    <div className="min-h-dvh bg-background">
      <SiteHeader />
      <main className="mx-auto w-full max-w-2xl px-4 py-12 md:py-20">
        <ErrorState retry={retry} />
      </main>
    </div>
  );
}
