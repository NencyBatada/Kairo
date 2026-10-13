import type { ReactNode } from "react";

import { Logo } from "@/components/logo";

export function SiteHeader({ children }: { children?: ReactNode }) {
  return (
    <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-sidebar px-4 md:px-6">
      <Logo href="/" />
      {children && <div className="flex items-center gap-2">{children}</div>}
    </header>
  );
}
