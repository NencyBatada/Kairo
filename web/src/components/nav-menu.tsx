import Link from "next/link";

const areas = [
  { segment: "today", label: "Today" },
  { segment: "opportunities", label: "Opportunities" },
  { segment: "campaigns", label: "Campaigns" },
  { segment: "triggers", label: "Triggers" },
  { segment: "impact-lab", label: "Impact Lab" },
  { segment: "brand-kit", label: "Brand Kit" },
  { segment: "settings", label: "Settings" },
];

export function NavMenu({ workspace }: { workspace: string }) {
  return (
    <nav aria-label="Main">
      <ul className="space-y-1">
        {areas.map((area) => (
          <li key={area.segment}>
            <Link
              href={`/${workspace}/${area.segment}`}
              className="block rounded-md px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
            >
              {area.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
