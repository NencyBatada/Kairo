"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef } from "react";

import { useLiveDispatch, type LiveWorkspace } from "@/components/workspace-live-provider";
import { parseAgentProgress } from "@/lib/agent-progress";
import { AGENT_PROGRESS_EVENT, liveStreamUrl, WORKSPACE_LIVE_EVENTS } from "@/lib/live-events";
import { navigateTo } from "@/lib/navigation";
import { ASK_EVENTS, parseAskEvent } from "@/lib/ask-kairo/model";

export const REFRESH_DELAY_MS = 100;
export const RECONNECT_BASE_DELAY_MS = 1_000;
export const RECONNECT_MAX_DELAY_MS = 30_000;

export function reconnectDelay(attempt: number, random: () => number = Math.random) {
  const ceiling = Math.min(RECONNECT_MAX_DELAY_MS, RECONNECT_BASE_DELAY_MS * 2 ** attempt);
  return Math.round(ceiling / 2 + random() * (ceiling / 2));
}

type Access = "allowed" | "signed-out" | "not-a-member" | "unknown";

async function checkAccess(brandId: string): Promise<Access> {
  try {
    const response = await fetch(`/api/brands/${brandId}`, { credentials: "same-origin", cache: "no-store" });
    if (response.status === 401) {
      return "signed-out";
    }
    if (response.status === 403 || response.status === 404) {
      return "not-a-member";
    }
    return response.ok ? "allowed" : "unknown";
  } catch {
    return "unknown";
  }
}

type WorkspaceLiveUpdatesProps = {
  brandId: string;
  workspace?: LiveWorkspace;
};

export function WorkspaceLiveUpdates({ brandId, workspace }: WorkspaceLiveUpdatesProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { reportProgress, setConnection, clearJobs, setWorkspace, publishAsk, hasAskDraft } = useLiveDispatch();
  const pathnameRef = useRef(pathname);
  useEffect(() => { setWorkspace(workspace ?? null); }, [workspace, setWorkspace]);

  useEffect(() => {
    pathnameRef.current = pathname;
  }, [pathname]);

  useEffect(() => {
    let disposed = false;
    let source: EventSource | null = null;
    let refreshTimer: ReturnType<typeof setTimeout> | null = null;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let attempt = 0;
    let catchUpOnOpen = false;

    clearJobs();
    setConnection("connecting");

    const scheduleRefresh = () => {
      if (refreshTimer) {
        clearTimeout(refreshTimer);
      }
      refreshTimer = setTimeout(() => {
        refreshTimer = null;
        router.refresh();
      }, REFRESH_DELAY_MS);
    };

    const onProgress = (event: Event) => {
      const progress = parseAgentProgress(String((event as MessageEvent).data ?? ""));
      if (progress) {
        reportProgress(progress);
      }
    };

    const recover = async () => {
      const access = await checkAccess(brandId);
      if (disposed) {
        return;
      }
      if (access === "signed-out" && !hasAskDraft()) {
        const returnUrl = pathnameRef.current || "/";
        router.replace(`/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`);
        return;
      }
      if (access === "not-a-member") {
        navigateTo("/");
        return;
      }
      const delay = reconnectDelay(attempt);
      attempt += 1;
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        catchUpOnOpen = true;
        open();
      }, delay);
    };

    const open = () => {
      const stream = new EventSource(liveStreamUrl(brandId));
      source = stream;

      for (const type of WORKSPACE_LIVE_EVENTS) {
        stream.addEventListener(type, scheduleRefresh);
      }
      for (const type of ASK_EVENTS) {
        stream.addEventListener(type, event => publishAsk(parseAskEvent(type, String((event as MessageEvent).data ?? ""))));
      }
      stream.addEventListener("reset", () => publishAsk(null));
      stream.addEventListener("reset", scheduleRefresh);
      stream.addEventListener(AGENT_PROGRESS_EVENT, onProgress);

      stream.onopen = () => {
        attempt = 0;
        setConnection("open");
        if (catchUpOnOpen) {
          catchUpOnOpen = false;
          scheduleRefresh();
        }
        publishAsk(null);
      };

      stream.onerror = () => {
        if (disposed || source !== stream) {
          return;
        }
        setConnection("reconnecting");
        if (stream.readyState !== EventSource.CLOSED) {
          return;
        }
        stream.close();
        source = null;
        void recover();
      };
    };

    open();

    return () => {
      disposed = true;
      if (refreshTimer) {
        clearTimeout(refreshTimer);
      }
      if (reconnectTimer) {
        clearTimeout(reconnectTimer);
      }
      source?.close();
      source = null;
    };
  }, [brandId, router, reportProgress, setConnection, clearJobs, publishAsk, hasAskDraft]);

  return null;
}
