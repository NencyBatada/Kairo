import { Suspense } from "react";

import { PageHeader } from "@/components/page-header";

import { SettingsNav } from "./_components/settings-nav";

export default function SettingsLayout({ children, params }: LayoutProps<"/[workspace]/settings">) {
  return (
    <div className="flex flex-col gap-8">
      <div>
        <PageHeader title="Settings" description="Workspace details, your team, who approves what, and the audit log." />
        <Suspense fallback={<div className="mt-6 h-11 border-b border-border" />}>
          <SettingsNavFor params={params} />
        </Suspense>
      </div>
      {children}
    </div>
  );
}

async function SettingsNavFor({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  return <SettingsNav workspace={workspace} />;
}
