import { SignOutButton } from "./sign-out-button";

export function UserMenu({ name, email }: { name: string; email: string }) {
  return (
    <div className="space-y-2 border-t border-black/10 px-3 pt-4 text-sm dark:border-white/15">
      <p className="font-medium">{name}</p>
      <p className="truncate text-zinc-600 dark:text-zinc-400">{email}</p>
      <SignOutButton />
    </div>
  );
}
