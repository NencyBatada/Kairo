import type { ReactNode } from "react";

import { AskKairoTrigger } from "@/components/ask-kairo-trigger";
import { cn } from "@/lib/utils";

type PageHeaderProps = {
  title: string;
  description?: string;
  eyebrow?: string;
  actions?: ReactNode;
  className?: string;
};

export function PageHeader({ title, description, eyebrow, actions, className }: PageHeaderProps) {
  return (
    <header className={cn("flex flex-col gap-4 md:flex-row md:flex-wrap md:items-end md:justify-between", className)}>
      <div className="min-w-0 md:basis-72 md:grow">
        {eyebrow && <p className="mb-2 text-eyebrow text-subtle-foreground uppercase">{eyebrow}</p>}
        <h1 className="font-display text-display-md text-foreground md:text-display-lg">{title}</h1>
        {description && <p className="mt-2 max-w-prose text-muted-foreground">{description}</p>}
      </div>
      <div className="flex min-w-0 max-w-full flex-wrap items-center gap-2">
        {actions}
        <AskKairoTrigger area={title} />
      </div>
    </header>
  );
}
