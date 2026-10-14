import type { ReactNode } from "react";

import { MobileNav } from "@/components/mobile-nav";
import { WorkspaceAssistant } from "@/components/workspace-assistant";

type AppShellProps = {
  sidebar: ReactNode;
  topBar?: ReactNode;
  children: ReactNode;
};

export function AppShell({ sidebar, topBar, children }: AppShellProps) {
  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[15.5rem_minmax(0,1fr)]">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-card text-sm font-semibold text-foreground shadow-popover focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:px-4 focus:py-2"
      >
        Skip to content
      </a>
      <aside className="sticky top-0 hidden h-dvh overflow-y-auto border-r border-border bg-sidebar lg:block">{sidebar}</aside>
      <MobileNav context={topBar}>{sidebar}</MobileNav>
      <WorkspaceAssistant>
        <main id="main" tabIndex={-1} className="min-w-0 px-4 py-6 focus-visible:outline-none md:px-6 md:py-8 lg:px-10 lg:py-10">
          <div className="mx-auto w-full max-w-300">{children}</div>
        </main>
      </WorkspaceAssistant>
    </div>
  );
}
