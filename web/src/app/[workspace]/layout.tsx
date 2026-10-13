import { Suspense } from "react";

import { WorkspaceSidebar } from "./_components/workspace-sidebar";

export default function WorkspaceLayout({ children, params }: LayoutProps<"/[workspace]">) {
  return (
    <div className="flex min-h-screen">
      <aside className="w-60 shrink-0 border-r border-black/10 p-4 dark:border-white/15">
        <p className="mb-6 px-3 text-lg font-semibold">Kairo</p>
        <Suspense fallback={null}>
          <WorkspaceSidebar params={params} />
        </Suspense>
      </aside>
      <main className="flex-1 p-8">
        <Suspense fallback={null}>{children}</Suspense>
      </main>
    </div>
  );
}
