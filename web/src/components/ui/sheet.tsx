"use client";

import { Dialog as SheetPrimitive } from "@base-ui/react/dialog";
import { X } from "lucide-react";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

type SheetSide = "left" | "right" | "bottom";

const sideClasses: Record<SheetSide, string> = {
  left: "inset-y-0 left-0 h-dvh w-[min(85vw,20rem)] border-r data-ending-style:-translate-x-full data-starting-style:-translate-x-full",
  right: "inset-y-0 right-0 h-dvh w-[min(85vw,20rem)] border-l data-ending-style:translate-x-full data-starting-style:translate-x-full",
  bottom:
    "inset-x-0 bottom-0 max-h-[90dvh] w-full overflow-y-auto rounded-t-xl border-t pb-[env(safe-area-inset-bottom)] max-lg:data-ending-style:translate-y-full max-lg:data-starting-style:translate-y-full lg:inset-0 lg:m-auto lg:h-fit lg:max-h-[calc(100dvh-2rem)] lg:w-[calc(100%-2rem)] lg:max-w-md lg:rounded-xl lg:border lg:pb-0 lg:data-ending-style:scale-95 lg:data-ending-style:opacity-0 lg:data-starting-style:scale-95 lg:data-starting-style:opacity-0",
};

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
}: WithClassName<SheetPrimitive.Popup.Props> & { side?: SheetSide }) {
  return (
    <SheetPrimitive.Portal>
      <SheetPrimitive.Backdrop className="fixed inset-0 z-50 bg-(--scrim) transition-opacity duration-240 ease-enter data-ending-style:opacity-0 data-ending-style:duration-160 data-ending-style:ease-exit data-starting-style:opacity-0" />
      <SheetPrimitive.Popup
        data-slot="sheet-content"
        data-side={side}
        className={cn(
          "fixed z-50 flex flex-col border-border bg-card text-card-foreground shadow-overlay outline-none transition-[translate,scale,opacity] duration-240 ease-enter data-ending-style:duration-160 data-ending-style:ease-exit",
          sideClasses[side],
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

function SheetDescription({ className, ...props }: WithClassName<SheetPrimitive.Description.Props>) {
  return <SheetPrimitive.Description data-slot="sheet-description" className={cn("text-sm text-muted-foreground", className)} {...props} />;
}

export { Sheet, SheetContent, SheetDescription, SheetTitle, SheetTrigger };
