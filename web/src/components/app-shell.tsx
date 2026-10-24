import type { ReactNode } from "react";

import { MobileNav } from "@/components/mobile-nav";
import { WorkspaceAssistant } from "@/components/workspace-assistant";
import { SkipLink } from "@/components/skip-link";

type AppShellProps = {
  sidebar: ReactNode;
  topBar?: ReactNode;
  topBarActions?: ReactNode;
  children: ReactNode;
};

export function AppShell({ sidebar, topBar, topBarActions, children }: AppShellProps) {
  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[15.5rem_minmax(0,1fr)]">
      <SkipLink />
      <aside className="sticky top-0 hidden h-dvh overflow-y-auto border-r border-border bg-sidebar lg:block">{sidebar}</aside>
      <MobileNav context={topBar} actions={topBarActions}>
        {sidebar}
      </MobileNav>
      <WorkspaceAssistant>
        <main id="main" tabIndex={-1} className="min-w-0 px-4 py-6 focus-visible:outline-none md:px-6 md:py-8 lg:px-10 lg:py-10">
          <div className="mx-auto w-full max-w-300">{children}</div>
        </main>
      </WorkspaceAssistant>
    </div>
  );
}
