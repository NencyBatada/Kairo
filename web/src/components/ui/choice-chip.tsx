import type { ComponentProps, ReactNode } from "react";

import { cn } from "@/lib/utils";

import { Checkbox } from "./checkbox";

type ChoiceChipProps = Omit<ComponentProps<"input">, "type" | "className"> & { className?: string; children: ReactNode };

function ChoiceChip({ className, children, ...props }: ChoiceChipProps) {
  return (
    <label
      data-slot="choice-chip"
      className={cn(
        "inline-flex h-8 cursor-pointer items-center gap-2 rounded-full border border-border-strong bg-card pr-3 pl-2 text-small font-medium text-muted-foreground transition-colors duration-120 select-none hover:bg-accent has-checked:border-primary/40 has-checked:bg-primary-soft has-checked:text-primary-strong has-disabled:cursor-not-allowed has-disabled:opacity-60 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-ring pointer-coarse:h-11",
        className,
      )}
    >
      <Checkbox className="[&>input]:focus-visible:outline-none" {...props} />
      {children}
    </label>
  );
}

export { ChoiceChip };
