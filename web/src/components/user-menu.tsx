"use client";

import { ChevronsUpDown, LogOut } from "lucide-react";

import { useSignOut } from "@/components/sign-out-button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { UserAvatar } from "@/components/user-avatar";

type UserMenuProps = { name: string; email: string; pictureUrl?: string | null };

export function UserMenu({ name, email, pictureUrl }: UserMenuProps) {
  const signOut = useSignOut();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={`Account: ${name}`}
        className="flex w-full cursor-pointer items-center gap-2.5 rounded-lg px-2 py-2 text-left transition-colors duration-120 hover:bg-accent data-popup-open:bg-accent"
      >
        <UserAvatar name={name} pictureUrl={pictureUrl} isYou />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-small font-semibold text-foreground" title={name}>
            {name}
          </span>
          <span className="block truncate text-xs text-subtle-foreground" title={email}>
            {email}
          </span>
        </span>
        <ChevronsUpDown className="size-4 shrink-0 text-subtle-foreground" aria-hidden="true" />
      </DropdownMenuTrigger>
      <DropdownMenuContent side="top" aria-label="Account" className="w-(--anchor-width) min-w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel>
            Signed in as
            <span className="block truncate text-small font-medium text-foreground">{email}</span>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={signOut}>
          <LogOut aria-hidden="true" />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
