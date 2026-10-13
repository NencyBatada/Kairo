import { cva, type VariantProps } from "class-variance-authority";
import { CircleAlert, CircleCheck, Info, TriangleAlert } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

const alertVariants = cva(
  "grid w-full grid-cols-[auto_minmax(0,1fr)] items-start gap-x-3 rounded-lg border px-4 py-3 text-sm [&>svg]:mt-0.5 [&>svg]:size-4",
  {
    variants: {
      variant: {
        neutral: "border-border bg-muted text-foreground [&>svg]:text-muted-foreground",
        info: "border-info/25 bg-info-soft text-info-strong [&>svg]:text-info",
        success: "border-success/25 bg-success-soft text-success-strong [&>svg]:text-success",
        warning: "border-warning/30 bg-warning-soft text-warning-strong [&>svg]:text-warning",
        destructive: "border-destructive/25 bg-destructive-soft text-destructive-strong [&>svg]:text-destructive",
      },
    },
    defaultVariants: {
      variant: "neutral",
    },
  },
);

const alertIcons = {
  neutral: Info,
  info: Info,
  success: CircleCheck,
  warning: TriangleAlert,
  destructive: CircleAlert,
};

function Alert({ className, variant = "neutral", role, children, ...props }: ComponentProps<"div"> & VariantProps<typeof alertVariants>) {
  const Icon = alertIcons[variant ?? "neutral"];

  return (
    <div
      data-slot="alert"
      role={role ?? (variant === "destructive" ? "alert" : undefined)}
      className={cn(alertVariants({ variant }), className)}
      {...props}
    >
      <Icon aria-hidden="true" />
      <div className="flex min-w-0 flex-col gap-0.5">{children}</div>
    </div>
  );
}

export { Alert };
