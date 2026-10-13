"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { cn } from "@/lib/utils";

export type LinkTab = { href: string; label: string; exact?: boolean };

export function isCurrentPath(pathname: string | null, href: string, exact = false) {
  if (!pathname) {
    return false;
  }

  return pathname === href || (!exact && pathname.startsWith(`${href}/`));
}

export function LinkTabs({ label, tabs, className }: { label: string; tabs: LinkTab[]; className?: string }) {
  const pathname = usePathname();

  return (
    <nav aria-label={label} className={cn("no-scrollbar -mx-4 -my-1 overflow-x-auto px-4 py-1 md:-mx-1 md:px-1", className)}>
      <ul className="flex min-w-max gap-6 border-b border-border">
        {tabs.map((tab) => (
          <li key={tab.href}>
            <Link
              href={tab.href}
              aria-current={isCurrentPath(pathname, tab.href, tab.exact) ? "page" : undefined}
              className="-mb-px inline-flex h-11 items-center border-b-2 border-transparent text-sm font-medium text-muted-foreground transition-colors duration-120 hover:text-foreground aria-[current=page]:border-primary aria-[current=page]:font-semibold aria-[current=page]:text-foreground"
            >
              {tab.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
