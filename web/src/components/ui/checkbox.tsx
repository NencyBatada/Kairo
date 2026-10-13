import { Check } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

type CheckboxProps = Omit<ComponentProps<"input">, "type" | "className"> & { className?: string };

function Checkbox({ className, ...props }: CheckboxProps) {
  return (
    <span data-slot="checkbox" className={cn("relative inline-grid size-4 shrink-0 place-items-center", className)}>
      <input
        type="checkbox"
        className="peer size-4 cursor-pointer appearance-none rounded-[4px] border border-input bg-card transition-colors duration-120 checked:border-primary checked:bg-primary disabled:cursor-not-allowed disabled:opacity-60 forced-colors:appearance-auto"
        {...props}
      />
      <Check
        className="pointer-events-none absolute size-3 text-primary-foreground opacity-0 [stroke-width:3] peer-checked:opacity-100 forced-colors:hidden"
        aria-hidden="true"
      />
    </span>
  );
}

export { Checkbox };
