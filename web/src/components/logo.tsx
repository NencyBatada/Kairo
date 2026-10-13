import Link from "next/link";

import { cn } from "@/lib/utils";

export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" className={cn("size-6 shrink-0", className)}>
      <rect width="24" height="24" rx="7" className="fill-primary" />
      <path
        d="M6.5 15.5l4-4 2.5 2.5 4.5-5.5"
        fill="none"
        strokeWidth="2.3"
        strokeLinecap="round"
        strokeLinejoin="round"
        className="stroke-primary-foreground"
      />
      <circle cx="17.5" cy="8.5" r="2.1" className="fill-primary-foreground" />
    </svg>
  );
}

export function Logo({ href, className }: { href?: string; className?: string }) {
  const classes = cn("inline-flex items-center gap-2.5 rounded-md text-foreground", className);
  const content = (
    <>
      <LogoMark />
      <span className="font-display text-display-sm leading-none">Kairo</span>
    </>
  );

  return href ? (
    <Link href={href} className={classes}>
      {content}
    </Link>
  ) : (
    <span className={classes}>{content}</span>
  );
}
