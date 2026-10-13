import Link from "next/link";

type WorkspaceLink = { id: string; handle: string; name: string };

type WorkspaceSwitcherProps = {
  current: WorkspaceLink;
  workspaces: WorkspaceLink[];
  pendingInvitations: number;
};

export function WorkspaceSwitcher({ current, workspaces, pendingInvitations }: WorkspaceSwitcherProps) {
  return (
    <details className="rounded-md border border-black/10 dark:border-white/15">
      <summary className="cursor-pointer px-3 py-2 text-sm font-medium">{current.name}</summary>
      <nav aria-label="Workspaces" className="border-t border-black/10 p-1 dark:border-white/15">
        <ul className="space-y-1">
          {workspaces.map((workspace) => (
            <li key={workspace.id}>
              <Link
                href={`/${workspace.handle}/today`}
                aria-current={workspace.id === current.id ? "page" : undefined}
                className="block rounded-md px-2 py-1 text-sm hover:bg-black/5 aria-[current=page]:font-semibold dark:hover:bg-white/10"
              >
                {workspace.name}
              </Link>
            </li>
          ))}
        </ul>
        <Link href="/welcome" className="mt-1 block rounded-md px-2 py-1 text-sm hover:bg-black/5 dark:hover:bg-white/10">
          {pendingInvitations > 0
            ? `Invitations (${pendingInvitations}) and new workspaces`
            : "Create a workspace"}
        </Link>
      </nav>
    </details>
  );
}
