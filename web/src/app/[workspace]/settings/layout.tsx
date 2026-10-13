import { Suspense } from "react";

import { SettingsNav } from "./_components/settings-nav";

export default function SettingsLayout({ children, params }: LayoutProps<"/[workspace]/settings">) {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Settings</h1>
      <Suspense fallback={null}>
        <SettingsNavFor params={params} />
      </Suspense>
      {children}
    </div>
  );
}

async function SettingsNavFor({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  return <SettingsNav workspace={workspace} />;
}
