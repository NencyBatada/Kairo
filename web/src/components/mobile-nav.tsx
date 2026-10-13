"use client";

import { Menu } from "lucide-react";
import { useState, type MouseEvent, type ReactNode } from "react";

import { Logo } from "@/components/logo";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";

export function MobileNav({ context, children }: { context?: ReactNode; children: ReactNode }) {
  const [open, setOpen] = useState(false);

  function closeOnLink(event: MouseEvent<HTMLDivElement>) {
    if ((event.target as Element).closest("a")) {
      setOpen(false);
    }
  }

  return (
    <header className="sticky top-0 z-40 flex h-14 items-center gap-2 border-b border-border bg-sidebar px-2 lg:hidden">
      <Sheet open={open} onOpenChange={setOpen}>
        <SheetTrigger
          aria-label="Open navigation"
          className="inline-grid size-11 shrink-0 cursor-pointer place-items-center rounded-md text-foreground transition-colors duration-120 hover:bg-accent"
        >
          <Menu className="size-5" aria-hidden="true" />
        </SheetTrigger>
        <SheetContent side="left" className="bg-sidebar">
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <div className="h-full overflow-y-auto" onClick={closeOnLink}>
            {children}
          </div>
        </SheetContent>
      </Sheet>
      <Logo href="/" />
      {context && <div className="ml-auto min-w-0 truncate pr-2 text-small font-semibold text-muted-foreground">{context}</div>}
    </header>
  );
}
