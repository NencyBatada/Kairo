"use client";

import { AlertDialog as AlertDialogPrimitive } from "@base-ui/react/alert-dialog";
import type { ComponentProps } from "react";

import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

function AlertDialog(props: AlertDialogPrimitive.Root.Props) {
  return <AlertDialogPrimitive.Root {...props} />;
}

function AlertDialogTrigger(props: AlertDialogPrimitive.Trigger.Props) {
  return <AlertDialogPrimitive.Trigger data-slot="alert-dialog-trigger" {...props} />;
}

function AlertDialogContent({ className, ...props }: WithClassName<AlertDialogPrimitive.Popup.Props>) {
  return (
    <AlertDialogPrimitive.Portal>
      <AlertDialogPrimitive.Backdrop className="fixed inset-0 z-50 bg-(--scrim) data-open:animate-in data-open:fade-in-0 data-open:duration-240 data-open:ease-enter data-closed:animate-out data-closed:fade-out-0 data-closed:duration-160 data-closed:ease-exit" />
      <AlertDialogPrimitive.Popup
        data-slot="alert-dialog-content"
        className={cn(
          "fixed top-1/2 left-1/2 z-50 grid w-[calc(100%-2rem)] max-w-md grid-cols-1 -translate-x-1/2 -translate-y-1/2 gap-5 rounded-xl border border-border bg-popover p-5 text-popover-foreground shadow-overlay outline-none data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-open:duration-240 data-open:ease-enter data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-closed:duration-160 data-closed:ease-exit",
          className,
        )}
        {...props}
      />
    </AlertDialogPrimitive.Portal>
  );
}

function AlertDialogHeader({ className, ...props }: ComponentProps<"div">) {
  return <div data-slot="alert-dialog-header" className={cn("grid grid-cols-1 gap-2", className)} {...props} />;
}

function AlertDialogFooter({ className, ...props }: ComponentProps<"div">) {
  return (
    <div data-slot="alert-dialog-footer" className={cn("flex flex-col-reverse gap-2 sm:flex-row sm:justify-end", className)} {...props} />
  );
}

function AlertDialogTitle({ className, ...props }: WithClassName<AlertDialogPrimitive.Title.Props>) {
  return (
    <AlertDialogPrimitive.Title data-slot="alert-dialog-title" className={cn("text-base font-semibold wrap-anywhere text-foreground", className)} {...props} />
  );
}

function AlertDialogDescription({ className, ...props }: WithClassName<AlertDialogPrimitive.Description.Props>) {
  return (
    <AlertDialogPrimitive.Description
      data-slot="alert-dialog-description"
      className={cn("text-sm wrap-anywhere text-muted-foreground", className)}
      {...props}
    />
  );
}

function AlertDialogCancel({ className, ...props }: WithClassName<AlertDialogPrimitive.Close.Props>) {
  return (
    <AlertDialogPrimitive.Close
      data-slot="alert-dialog-cancel"
      type="button"
      className={cn(buttonVariants({ variant: "secondary" }), className)}
      {...props}
    />
  );
}

export {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
};
