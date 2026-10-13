import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

function Field({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="field" className={cn("flex min-w-0 flex-col gap-1.5", className)} {...props} />;
}

function FieldDescription({ className, ...props }: ComponentProps<"p">) {
  return <p data-slot="field-description" className={cn("text-small text-subtle-foreground", className)} {...props} />;
}

export { Field, FieldDescription };
