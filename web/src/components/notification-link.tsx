"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTransition, type MouseEvent, type ReactNode } from "react";

type NotificationLinkProps = {
  href: string;
  unread: boolean;
  markRead: () => Promise<unknown>;
  className?: string;
  children: ReactNode;
};

export function NotificationLink({ href, unread, markRead, className, children }: NotificationLinkProps) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  function open(event: MouseEvent<HTMLAnchorElement>) {
    if (!unread || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }

    event.preventDefault();
    startTransition(async () => {
      await markRead().catch(() => undefined);
      router.push(href);
    });
  }

  return (
    <Link href={href} onClick={open} aria-busy={pending || undefined} className={className}>
      {children}
    </Link>
  );
}
