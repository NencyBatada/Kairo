import { Suspense } from "react";

import { AppShell } from "@/components/app-shell";
import { PageSkeleton, SidebarSkeleton } from "@/components/skeletons";

import { WorkspaceName } from "./_components/workspace-name";
import { WorkspaceSidebar } from "./_components/workspace-sidebar";

export default function WorkspaceLayout({ children, params }: LayoutProps<"/[workspace]">) {
  return (
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
    >
      <Suspense fallback={<PageSkeleton />}>{children}</Suspense>
    </AppShell>
  );
}
