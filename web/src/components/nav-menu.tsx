"use client";

import { FlaskConical, Megaphone, Palette, Settings, Sun, TrendingUp, Zap, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { isCurrentPath } from "@/components/link-tabs";

const areas: { segment: string; label: string; icon: LucideIcon }[] = [
  { segment: "today", label: "Today", icon: Sun },
  { segment: "opportunities", label: "Opportunities", icon: TrendingUp },
  { segment: "campaigns", label: "Campaigns", icon: Megaphone },
  { segment: "triggers", label: "Triggers", icon: Zap },
  { segment: "impact-lab", label: "Impact Lab", icon: FlaskConical },
  { segment: "brand-kit", label: "Brand Kit", icon: Palette },
  { segment: "settings", label: "Settings", icon: Settings },
];

export function NavMenu({ workspace }: { workspace: string }) {
  const pathname = usePathname();

  return (
    <nav aria-label="Main">
      <ul className="flex flex-col gap-0.5">
        {areas.map(({ segment, label, icon: Icon }) => {
          const href = `/${workspace}/${segment}`;

          return (
            <li key={segment}>
              <Link
                href={href}
                aria-current={isCurrentPath(pathname, href) ? "page" : undefined}
                className="group flex h-9 items-center gap-3 rounded-md px-2.5 text-sm font-medium text-muted-foreground transition-colors duration-120 hover:bg-accent hover:text-foreground aria-[current=page]:bg-primary-soft aria-[current=page]:font-semibold aria-[current=page]:text-foreground pointer-coarse:h-11"
              >
                <Icon className="size-4 text-subtle-foreground group-aria-[current=page]:text-primary" aria-hidden="true" />
                {label}
              </Link>
              {segment === "brand-kit" && isCurrentPath(pathname, href) && (
                <ul aria-label="Brand Kit pages" className="mt-1 ml-7 border-l border-border pl-2">
                  <li><Link href={`${href}/catalogue`} aria-current={isCurrentPath(pathname, `${href}/catalogue`) ? "page" : undefined} className="flex min-h-9 items-center rounded-md px-2 text-small text-muted-foreground hover:bg-accent hover:text-foreground aria-[current=page]:font-semibold aria-[current=page]:text-primary pointer-coarse:min-h-11">Catalogue</Link></li>
                  <li><Link href={`${href}/memory`} aria-current={isCurrentPath(pathname, `${href}/memory`) ? "page" : undefined} className="flex min-h-9 items-center rounded-md px-2 text-small text-muted-foreground hover:bg-accent hover:text-foreground aria-[current=page]:font-semibold aria-[current=page]:text-primary pointer-coarse:min-h-11">Brand Memory</Link></li>
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
