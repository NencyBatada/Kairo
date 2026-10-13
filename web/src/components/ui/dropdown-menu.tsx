"use client";

import { Menu as MenuPrimitive } from "@base-ui/react/menu";
import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

type WithClassName<T> = Omit<T, "className"> & { className?: string };

const itemClassName =
  "relative flex min-h-9 cursor-default items-center gap-2.5 rounded-md px-2.5 text-sm text-foreground outline-none select-none data-disabled:pointer-events-none data-disabled:opacity-50 data-highlighted:bg-accent data-highlighted:shadow-[inset_3px_0_0_var(--color-primary)] pointer-coarse:min-h-11 [&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-subtle-foreground";

function DropdownMenu(props: MenuPrimitive.Root.Props) {
  return <MenuPrimitive.Root {...props} />;
}

function DropdownMenuTrigger({ className, ...props }: WithClassName<MenuPrimitive.Trigger.Props>) {
  return <MenuPrimitive.Trigger data-slot="dropdown-menu-trigger" className={className} {...props} />;
}

function DropdownMenuContent({
  align = "start",
  side = "bottom",
  sideOffset = 6,
  className,
  ...props
}: WithClassName<MenuPrimitive.Popup.Props> & Pick<MenuPrimitive.Positioner.Props, "align" | "side" | "sideOffset">) {
  return (
    <MenuPrimitive.Portal>
      <MenuPrimitive.Positioner className="z-50 outline-none" align={align} side={side} sideOffset={sideOffset}>
        <MenuPrimitive.Popup
          data-slot="dropdown-menu-content"
          className={cn(
            "max-h-(--available-height) min-w-(--anchor-width) origin-(--transform-origin) overflow-y-auto rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-popover outline-none data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-open:duration-180 data-open:ease-enter data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-closed:duration-120 data-closed:ease-exit",
            className,
          )}
          {...props}
        />
      </MenuPrimitive.Positioner>
    </MenuPrimitive.Portal>
  );
}

function DropdownMenuGroup(props: MenuPrimitive.Group.Props) {
  return <MenuPrimitive.Group {...props} />;
}

function DropdownMenuLabel({ className, ...props }: WithClassName<MenuPrimitive.GroupLabel.Props>) {
  return (
    <MenuPrimitive.GroupLabel
      data-slot="dropdown-menu-label"
      className={cn("px-2.5 pt-2 pb-1 text-xs font-medium text-subtle-foreground", className)}
      {...props}
    />
  );
}

function DropdownMenuItem({ className, ...props }: WithClassName<MenuPrimitive.Item.Props>) {
  return <MenuPrimitive.Item data-slot="dropdown-menu-item" className={cn(itemClassName, className)} {...props} />;
}

function DropdownMenuLinkItem({ className, ...props }: WithClassName<ComponentProps<typeof MenuPrimitive.LinkItem>>) {
  return <MenuPrimitive.LinkItem data-slot="dropdown-menu-link-item" className={cn(itemClassName, className)} {...props} />;
}

function DropdownMenuSeparator({ className, ...props }: WithClassName<MenuPrimitive.Separator.Props>) {
  return <MenuPrimitive.Separator className={cn("-mx-1 my-1 h-px bg-border", className)} {...props} />;
}

export {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuLinkItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
};
