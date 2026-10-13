"use client";

import { Dialog as SheetPrimitive } from "@base-ui/react/dialog";
import { X } from "lucide-react";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

function Sheet(props: SheetPrimitive.Root.Props) {
  return <SheetPrimitive.Root {...props} />;
}

function SheetTrigger({ className, ...props }: WithClassName<SheetPrimitive.Trigger.Props>) {
  return <SheetPrimitive.Trigger data-slot="sheet-trigger" className={className} {...props} />;
}

function SheetContent({
  className,
  children,
  side = "left",
  ...props
}: WithClassName<SheetPrimitive.Popup.Props> & { side?: "left" | "right" }) {
  return (
    <SheetPrimitive.Portal>
      <SheetPrimitive.Backdrop className="fixed inset-0 z-50 bg-(--scrim) transition-opacity duration-240 data-ending-style:opacity-0 data-ending-style:duration-160 data-starting-style:opacity-0" />
      <SheetPrimitive.Popup
        data-slot="sheet-content"
        data-side={side}
        className={cn(
          "fixed inset-y-0 z-50 flex h-dvh w-[min(85vw,20rem)] flex-col border-border bg-card text-card-foreground shadow-overlay outline-none transition-transform duration-240 ease-[cubic-bezier(0.2,0,0,1)] data-ending-style:duration-160 data-ending-style:ease-[cubic-bezier(0.4,0,1,1)] data-[side=left]:left-0 data-[side=left]:border-r data-[side=left]:data-ending-style:-translate-x-full data-[side=left]:data-starting-style:-translate-x-full data-[side=right]:right-0 data-[side=right]:border-l data-[side=right]:data-ending-style:translate-x-full data-[side=right]:data-starting-style:translate-x-full",
          className,
        )}
        {...props}
      >
        {children}
        <SheetPrimitive.Close
          aria-label="Close"
          className="absolute top-3 right-3 inline-grid size-9 place-items-center rounded-md text-muted-foreground transition-colors duration-120 hover:bg-accent hover:text-foreground pointer-coarse:size-11"
        >
          <X className="size-4" aria-hidden="true" />
        </SheetPrimitive.Close>
      </SheetPrimitive.Popup>
    </SheetPrimitive.Portal>
  );
}

function SheetTitle({ className, ...props }: WithClassName<SheetPrimitive.Title.Props>) {
  return <SheetPrimitive.Title data-slot="sheet-title" className={cn("text-base font-semibold text-foreground", className)} {...props} />;
}

export { Sheet, SheetContent, SheetTitle, SheetTrigger };
