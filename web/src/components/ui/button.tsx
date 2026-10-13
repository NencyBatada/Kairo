import { cva, type VariantProps } from "class-variance-authority";
import { LoaderCircle } from "lucide-react";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-md border border-transparent text-sm font-semibold whitespace-nowrap transition-[color,background-color,border-color,scale] duration-120 ease-out select-none disabled:pointer-events-none disabled:opacity-55 aria-busy:cursor-progress [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-primary-hover",
        secondary: "border-border-strong bg-card text-foreground hover:bg-accent",
        ghost: "text-muted-foreground hover:bg-accent hover:text-foreground",
        destructive: "text-destructive hover:bg-destructive-soft",
        "destructive-solid": "bg-destructive text-destructive-foreground hover:bg-destructive-hover",
        link: "text-primary underline-offset-4 hover:underline",
      },
      size: {
        sm: "h-8 px-3 text-small pointer-coarse:h-11",
        default: "h-9 px-4 pointer-coarse:h-11",
        lg: "h-10 px-5 pointer-coarse:h-11",
        icon: "size-9 pointer-coarse:size-11",
      },
    },
    compoundVariants: [
      { variant: "link", class: "h-auto px-0 pointer-coarse:h-auto" },
      { variant: ["default", "secondary", "ghost", "destructive", "destructive-solid"], class: "motion-safe:active:scale-[0.98]" },
    ],
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

type ButtonProps = ComponentProps<"button"> & VariantProps<typeof buttonVariants> & { pending?: boolean };

function Button({ className, variant, size, pending = false, disabled, type = "button", children, ...props }: ButtonProps) {
  return (
    <button
      type={type}
      data-slot="button"
      className={cn(buttonVariants({ variant, size }), className)}
      disabled={disabled || pending}
      aria-busy={pending || undefined}
      {...props}
    >
      {pending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
      {children}
    </button>
  );
}

export { Button, buttonVariants };
