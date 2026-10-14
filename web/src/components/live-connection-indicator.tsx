"use client";

import { RefreshCw } from "lucide-react";
import { useEffect, useState } from "react";

import { useLiveConnection } from "@/components/workspace-live-provider";

export const RECONNECTING_NOTICE_DELAY_MS = 3_000;

export function LiveConnectionIndicator() {
  const connection = useLiveConnection();
  const [noticeShown, setNoticeShown] = useState(false);
  const reconnecting = connection === "reconnecting";

  useEffect(() => {
    if (!reconnecting) {
      return;
    }
    const timer = setTimeout(() => setNoticeShown(true), RECONNECTING_NOTICE_DELAY_MS);
    return () => {
      clearTimeout(timer);
      setNoticeShown(false);
    };
  }, [reconnecting]);

  const visible = reconnecting && noticeShown;

  return (
    <div
      role="status"
      className="pointer-events-none fixed inset-x-0 bottom-[calc(1rem+env(safe-area-inset-bottom))] z-40 flex justify-center px-4"
    >
      {visible && (
        <p className="flex items-center gap-2 rounded-full border border-border bg-card px-3 py-1.5 text-xs font-medium text-muted-foreground shadow-popover">
          <RefreshCw className="size-3.5 shrink-0 text-info motion-safe:animate-spin" aria-hidden="true" />
          Reconnecting to live updates…
        </p>
      )}
    </div>
  );
}
