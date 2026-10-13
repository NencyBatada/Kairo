"use client";

import { Avatar as AvatarPrimitive } from "@base-ui/react/avatar";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

function Avatar({ className, ...props }: WithClassName<AvatarPrimitive.Root.Props>) {
  return (
    <AvatarPrimitive.Root
      data-slot="avatar"
      className={cn("relative inline-flex size-8 shrink-0 overflow-hidden rounded-full bg-accent align-middle select-none", className)}
      {...props}
    />
  );
}

function AvatarImage({ className, ...props }: WithClassName<AvatarPrimitive.Image.Props>) {
  return <AvatarPrimitive.Image data-slot="avatar-image" className={cn("size-full object-cover", className)} {...props} />;
}

function AvatarFallback({ className, ...props }: WithClassName<AvatarPrimitive.Fallback.Props>) {
  return (
    <AvatarPrimitive.Fallback
      data-slot="avatar-fallback"
      className={cn("flex size-full items-center justify-center text-xs font-semibold text-muted-foreground", className)}
      {...props}
    />
  );
}

export { Avatar, AvatarFallback, AvatarImage };
