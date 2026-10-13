import Link from "next/link";

const sections = [
  { segment: "", label: "Workspace" },
  { segment: "/team", label: "Team" },
  { segment: "/approvals", label: "Approvals" },
  { segment: "/audit-log", label: "Audit log" },
];

export function SettingsNav({ workspace }: { workspace: string }) {
  return (
    <nav aria-label="Settings">
      <ul className="flex gap-6 border-b border-black/10 text-sm dark:border-white/15">
        {sections.map((section) => (
          <li key={section.label}>
            <Link href={`/${workspace}/settings${section.segment}`} className="block pb-2 hover:underline">
              {section.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
