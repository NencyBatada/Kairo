import type { Metadata } from "next";

import { NotFoundState } from "@/components/not-found-state";
import { SiteHeader } from "@/components/site-header";

export const metadata: Metadata = { title: "Page not found" };

export default function NotFound() {
  return (
    <div className="min-h-dvh bg-background">
      <SiteHeader />
      <main className="mx-auto w-full max-w-2xl px-4 py-12 md:py-20">
        <NotFoundState />
      </main>
    </div>
  );
}
