import { render, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";

import { WORKSPACE_LIVE_EVENTS } from "@/lib/live-events";

import { WorkspaceLiveUpdates } from "./workspace-live-updates";

const router = vi.hoisted(() => ({
  refresh: vi.fn(),
  replace: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => router,
  usePathname: () => "/acme/today",
}));

type Listener = (event: Event) => void;

const CONNECTING = 0;
const CLOSED = 2;

class MockEventSource {
  static CONNECTING = CONNECTING;
  static CLOSED = CLOSED;
  static instances: MockEventSource[] = [];
  url: string;
  readyState = CONNECTING;
  onerror: ((event: Event) => void) | null = null;
  private listeners = new Map<string, Set<Listener>>();

  constructor(url: string) {
    this.url = url;
    MockEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: Listener) {
    const set = this.listeners.get(type) ?? new Set();
    set.add(listener);
    this.listeners.set(type, set);
  }

  close() {
    this.readyState = CLOSED;
  }

  emit(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new Event(type));
    }
  }

  failClosed() {
    this.readyState = CLOSED;
    this.onerror?.(new Event("error"));
  }
}

Object.assign(MockEventSource, { CONNECTING, CLOSED });

beforeEach(() => {
  MockEventSource.instances = [];
  router.refresh.mockReset();
  router.replace.mockReset();
  vi.stubGlobal(
    "EventSource",
    MockEventSource as unknown as typeof EventSource,
  );
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(null, { status: 200 })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

test("opens one stream per workspace and refreshes when a live event arrives", async () => {
  vi.useFakeTimers();
  render(<WorkspaceLiveUpdates brandId="brand-1" />);

  expect(MockEventSource.instances).toHaveLength(1);
  expect(MockEventSource.instances[0].url).toBe("/api/brands/brand-1/live");

  for (const type of WORKSPACE_LIVE_EVENTS) {
    MockEventSource.instances[0].emit(type);
  }
  MockEventSource.instances[0].emit("reset");

  await vi.advanceTimersByTimeAsync(100);
  expect(router.refresh).toHaveBeenCalledOnce();
});

test("sends the user to sign-in when the stream closes after losing access", async () => {
  vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 401 }));

  render(<WorkspaceLiveUpdates brandId="brand-1" />);
  MockEventSource.instances[0].failClosed();

  await waitFor(() =>
    expect(router.replace).toHaveBeenCalledWith("/sign-in?returnUrl=%2Facme%2Ftoday"),
  );
});

test("closes the stream when the workspace changes", () => {
  const { rerender } = render(<WorkspaceLiveUpdates brandId="brand-1" />);
  const first = MockEventSource.instances[0];

  rerender(<WorkspaceLiveUpdates brandId="brand-2" />);

  expect(first.readyState).toBe(CLOSED);
  expect(MockEventSource.instances).toHaveLength(2);
  expect(MockEventSource.instances[1].url).toBe("/api/brands/brand-2/live");
});
