import { Suspense } from "react";

import { AppShell } from "@/components/app-shell";
import { LiveConnectionIndicator } from "@/components/live-connection-indicator";
import { PageSkeleton, SidebarSkeleton } from "@/components/skeletons";
import { WorkspaceLiveProvider } from "@/components/workspace-live-provider";

import { WorkspaceLiveBridge } from "./_components/workspace-live-bridge";
import { WorkspaceName } from "./_components/workspace-name";
import { WorkspaceNotifications } from "./_components/workspace-notifications";
import { WorkspaceSidebar } from "./_components/workspace-sidebar";

export default function WorkspaceLayout({ children, params }: LayoutProps<"/[workspace]">) {
  return (
    <WorkspaceLiveProvider>
      <AppShell
        sidebar={
          <Suspense fallback={<SidebarSkeleton />}>
            <WorkspaceSidebar params={params} />
          </Suspense>
        }
        topBar={
          <Suspense fallback={null}>
            <WorkspaceName params={params} />
          </Suspense>
        }
        topBarActions={
          <Suspense fallback={null}>
            <WorkspaceNotifications params={params} />
          </Suspense>
        }
      >
        <Suspense fallback={null}>
          <WorkspaceLiveBridge params={params} />
        </Suspense>
        <Suspense fallback={<PageSkeleton />}>{children}</Suspense>
      </AppShell>
      <LiveConnectionIndicator />
    </WorkspaceLiveProvider>
  );
}
