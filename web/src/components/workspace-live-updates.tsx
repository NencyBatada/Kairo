"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef } from "react";

import { liveStreamUrl, WORKSPACE_LIVE_EVENTS } from "@/lib/live-events";

type WorkspaceLiveUpdatesProps = {
  brandId: string;
};

export function WorkspaceLiveUpdates({ brandId }: WorkspaceLiveUpdatesProps) {
  const router = useRouter();
  const pathname = usePathname();
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pathnameRef = useRef(pathname);

  useEffect(() => {
    pathnameRef.current = pathname;
  }, [pathname]);

  useEffect(() => {
    const scheduleRefresh = () => {
      if (refreshTimer.current) {
        clearTimeout(refreshTimer.current);
      }
      refreshTimer.current = setTimeout(() => {
        refreshTimer.current = null;
        router.refresh();
      }, 100);
    };

    const redirectToSignIn = () => {
      const returnUrl = pathnameRef.current || "/";
      router.replace(`/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`);
    };

    const checkAccess = async () => {
      const response = await fetch(`/api/brands/${brandId}`, { credentials: "same-origin", cache: "no-store" });
      if (response.status === 401 || response.status === 404) {
        redirectToSignIn();
        return false;
      }
      return true;
    };

    const source = new EventSource(liveStreamUrl(brandId));

    for (const type of WORKSPACE_LIVE_EVENTS) {
      source.addEventListener(type, scheduleRefresh);
    }
    source.addEventListener("reset", scheduleRefresh);

    source.onerror = () => {
      if (source.readyState !== EventSource.CLOSED) {
        return;
      }
      void checkAccess();
    };

    return () => {
      if (refreshTimer.current) {
        clearTimeout(refreshTimer.current);
      }
      source.close();
    };
  }, [brandId, router]);

  return null;
}
