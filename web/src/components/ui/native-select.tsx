import { ChevronDown } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function NativeSelect({ className, ...props }: ComponentProps<"select">) {
  return (
    <div data-slot="native-select" className={cn("relative w-full", className)}>
      <select
        className="h-9 w-full min-w-0 cursor-pointer appearance-none rounded-md border border-input bg-card py-0 pr-9 pl-3 text-base text-foreground transition-colors duration-120 disabled:cursor-not-allowed disabled:bg-muted disabled:text-muted-foreground aria-invalid:border-destructive sm:text-sm pointer-coarse:h-11"
        {...props}
      />
      <ChevronDown
        className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-subtle-foreground"
        aria-hidden="true"
      />
    </div>
  );
}

export { NativeSelect };
