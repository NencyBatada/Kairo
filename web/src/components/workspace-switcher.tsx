"use client";

import { Check, ChevronsUpDown, Plus } from "lucide-react";
import Link from "next/link";

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuLinkItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { initial } from "@/lib/initials";

type WorkspaceLink = { id: string; handle: string; name: string };

type WorkspaceSwitcherProps = {
  current: WorkspaceLink;
  workspaces: WorkspaceLink[];
  pendingInvitations: number;
};

export function WorkspaceSwitcher({ current, workspaces, pendingInvitations }: WorkspaceSwitcherProps) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        title={current.name}
        aria-label={`Workspace: ${current.name}`}
        className="flex h-10 w-full cursor-pointer items-center gap-2.5 rounded-lg border border-border-strong bg-card px-2.5 text-left text-sm font-semibold text-foreground transition-colors duration-120 hover:bg-accent data-popup-open:bg-accent pointer-coarse:h-11"
      >
        <span aria-hidden="true" className="grid size-6 shrink-0 place-items-center rounded-md bg-primary-soft text-xs font-bold text-primary-strong">
          {initial(current.name)}
        </span>
        <span className="min-w-0 flex-1 truncate">{current.name}</span>
        <ChevronsUpDown className="size-4 shrink-0 text-subtle-foreground" aria-hidden="true" />
      </DropdownMenuTrigger>
      <DropdownMenuContent aria-label="Workspaces" className="w-(--anchor-width) max-w-(--available-width) min-w-72">
        <DropdownMenuGroup>
          <DropdownMenuLabel>Workspaces</DropdownMenuLabel>
          {workspaces.map((workspace) => (
            <DropdownMenuLinkItem
              key={workspace.id}
              render={<Link href={`/${workspace.handle}/today`} />}
              aria-current={workspace.id === current.id ? "page" : undefined}
              title={workspace.name}
            >
              <span className="min-w-0 flex-1 truncate">{workspace.name}</span>
              {workspace.id === current.id && <Check aria-hidden="true" />}
            </DropdownMenuLinkItem>
          ))}
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuLinkItem render={<Link href="/welcome" />}>
          <Plus aria-hidden="true" />
          {pendingInvitations > 0 ? `Invitations (${pendingInvitations}) and new workspaces` : "Create a workspace"}
        </DropdownMenuLinkItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
