import Link from "next/link";

const areas = [
  { href: "/today", label: "Today" },
  { href: "/opportunities", label: "Opportunities" },
  { href: "/campaigns", label: "Campaigns" },
  { href: "/triggers", label: "Triggers" },
  { href: "/impact-lab", label: "Impact Lab" },
  { href: "/brand-kit", label: "Brand Kit" },
  { href: "/settings", label: "Settings" },
];

export function NavMenu() {
  return (
    <nav aria-label="Main">
      <ul className="space-y-1">
        {areas.map((area) => (
          <li key={area.href}>
            <Link
              href={area.href}
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
