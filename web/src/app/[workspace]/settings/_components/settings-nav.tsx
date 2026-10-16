import { LinkTabs } from "@/components/link-tabs";

export function SettingsNav({ workspace }: { workspace: string }) {
  const base = `/${workspace}/settings`;

  return (
    <LinkTabs
      label="Settings"
      className="mt-6"
      tabs={[
        { href: base, label: "Workspace", exact: true },
        { href: `${base}/connections`, label: "Connections" },
        { href: `${base}/team`, label: "Team" },
        { href: `${base}/approvals`, label: "Approvals" },
        { href: `${base}/checks`, label: "Brand Guardian" },
        { href: `${base}/notifications`, label: "Notifications" },
        { href: `${base}/audit-log`, label: "Audit log" },
      ]}
    />
  );
}
