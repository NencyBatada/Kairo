import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

type EmptyStateProps = {
  icon: LucideIcon;
  title: string;
  description: string;
  as?: "h1" | "h2";
  children?: ReactNode;
  className?: string;
};

export function EmptyState({ icon: Icon, title, description, as: Heading = "h2", children, className }: EmptyStateProps) {
  return (
    <div className={cn("flex flex-col items-center rounded-lg border border-border bg-card px-6 py-14 text-center md:py-20", className)}>
      <div className="mb-5 grid size-12 place-items-center rounded-xl bg-primary-soft text-primary">
        <Icon className="size-6" aria-hidden="true" />
      </div>
      <Heading className="font-display text-display-md text-foreground">{title}</Heading>
      <p className="mt-2 max-w-[52ch] text-muted-foreground">{description}</p>
      {children && <div className="mt-6 flex flex-wrap justify-center gap-2">{children}</div>}
    </div>
  );
}
