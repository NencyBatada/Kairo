import { cva, type VariantProps } from "class-variance-authority";
import { CircleCheck, CircleX, Info, TriangleAlert } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

const badgeVariants = cva(
  "inline-flex h-6 w-fit shrink-0 items-center gap-1.5 rounded-full px-2.5 text-xs font-medium whitespace-nowrap [&_svg]:size-3.5 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        neutral: "bg-accent text-muted-foreground",
        gate: "bg-primary-soft text-primary-strong",
        success: "bg-success-soft text-success-strong",
        warning: "bg-warning-soft text-warning-strong",
        destructive: "bg-destructive-soft text-destructive-strong",
        info: "bg-info-soft text-info-strong",
      },
    },
    defaultVariants: {
      variant: "neutral",
    },
  },
);

const statusIcons = {
  success: CircleCheck,
  warning: TriangleAlert,
  destructive: CircleX,
  info: Info,
};

function Badge({ className, variant = "neutral", children, ...props }: ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  const Icon = variant && variant in statusIcons ? statusIcons[variant as keyof typeof statusIcons] : null;

  return (
    <span data-slot="badge" className={cn(badgeVariants({ variant }), className)} {...props}>
      {Icon && <Icon aria-hidden="true" />}
      {children}
    </span>
  );
}

export { Badge, badgeVariants };
