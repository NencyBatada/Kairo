import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Textarea({ className, ...props }: ComponentProps<"textarea">) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        "field-sizing-content min-h-24 w-full min-w-0 resize-y rounded-md border border-input bg-card px-3 py-2 text-base wrap-anywhere text-foreground transition-colors duration-120 placeholder:text-subtle-foreground disabled:cursor-not-allowed disabled:bg-muted disabled:text-muted-foreground aria-invalid:border-destructive sm:text-sm",
        className,
      )}
      {...props}
    />
  );
}

export { Textarea };
