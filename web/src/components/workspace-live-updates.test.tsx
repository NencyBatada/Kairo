import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";

import { useAgentJobs, useAskSessionRecovery, useAskUpdates, useLiveConnection, WorkspaceLiveProvider } from "@/components/workspace-live-provider";
import { WORKSPACE_LIVE_EVENTS } from "@/lib/live-events";

import { RECONNECT_MAX_DELAY_MS, reconnectDelay, WorkspaceLiveUpdates } from "./workspace-live-updates";
import { conversationId, messageId } from "@/lib/ask-kairo/fixtures.test-data";
import { ASK_EVENTS } from "@/lib/ask-kairo/model";

const askListener = vi.fn();
function AskProbe() { useAskUpdates(askListener); return null; }
function DraftProbe() { useAskSessionRecovery(true); return null; }

const router = vi.hoisted(() => ({
  refresh: vi.fn(),
  replace: vi.fn(),
}));

const navigation = vi.hoisted(() => ({ navigateTo: vi.fn() }));

vi.mock("next/navigation", () => ({
  useRouter: () => router,
  usePathname: () => "/acme/today",
}));

vi.mock("@/lib/navigation", () => navigation);

type Listener = (event: Event) => void;

const CONNECTING = 0;
const OPEN = 1;
const CLOSED = 2;

class MockEventSource {
  static CONNECTING = CONNECTING;
  static OPEN = OPEN;
  static CLOSED = CLOSED;
  static instances: MockEventSource[] = [];
  url: string;
  readyState = CONNECTING;
  onerror: ((event: Event) => void) | null = null;
  onopen: ((event: Event) => void) | null = null;
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

  open() {
    this.readyState = OPEN;
    this.onopen?.(new Event("open"));
  }

  emit(type: string, data = "{}") {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new MessageEvent(type, { data }));
    }
  }

  failConnecting() {
    this.readyState = CONNECTING;
    this.onerror?.(new Event("error"));
  }

  failClosed() {
    this.readyState = CLOSED;
    this.onerror?.(new Event("error"));
  }
}

function latest() {
  return MockEventSource.instances[MockEventSource.instances.length - 1];
}

function Probe() {
  const jobs = useAgentJobs();
  const connection = useLiveConnection();
  return (
    <div>
      <p data-testid="connection">{connection}</p>
      <ul>
        {jobs.map((job) => (
          <li key={job.jobId}>
            {job.agent}: {job.status} {job.step}
          </li>
        ))}
      </ul>
    </div>
  );
}

function renderLive(brandId = "brand-1") {
  const view = render(
    <WorkspaceLiveProvider>
      <WorkspaceLiveUpdates brandId={brandId} />
      <Probe />
    </WorkspaceLiveProvider>,
  );
  return {
    ...view,
    switchTo: (next: string) =>
      view.rerender(
        <WorkspaceLiveProvider>
          <WorkspaceLiveUpdates brandId={next} />
          <Probe />
        </WorkspaceLiveProvider>,
      ),
  };
}

function progress(overrides: Record<string, unknown> = {}) {
  return JSON.stringify({
    jobId: "job-1",
    agent: "create",
    status: "running",
    step: "Generating video",
    stepNumber: 2,
    stepCount: 5,
    message: null,
    subject: { kind: "campaign", id: "campaign-1" },
    at: "2026-10-14T09:05:00Z",
    ...overrides,
  });
}

beforeEach(() => {
  askListener.mockReset();
  MockEventSource.instances = [];
  router.refresh.mockReset();
  router.replace.mockReset();
  navigation.navigateTo.mockReset();
  vi.spyOn(Math, "random").mockReturnValue(1);
  vi.stubGlobal("EventSource", MockEventSource as unknown as typeof EventSource);
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(null, { status: 200 })),
  );
});

test("Ask events use the existing stream without refreshing the page for every segment", async () => {
  vi.useFakeTimers();
  render(<WorkspaceLiveProvider><WorkspaceLiveUpdates brandId="brand-1" /><AskProbe /></WorkspaceLiveProvider>);
  const payload = { conversationId, messageId, index: 0, segment: { text: "The first part", evidenceIds: [] } };
  act(() => latest().emit("ask.answer.segment", JSON.stringify(payload)));
  expect(askListener).toHaveBeenCalledWith({ ...payload, type: "ask.answer.segment" });
  expect(MockEventSource.instances).toHaveLength(1);
  await vi.advanceTimersByTimeAsync(100);
  expect(router.refresh).not.toHaveBeenCalled();
  for (const type of ASK_EVENTS) act(() => latest().emit(type, "{invalid"));
  expect(askListener).toHaveBeenLastCalledWith(null);
  act(() => latest().open());
  expect(askListener).toHaveBeenLastCalledWith(null);
});

test("an unsent Ask draft stays available for sign-in recovery instead of a redirect", async () => {
  vi.useFakeTimers();
  vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 401 })));
  render(<WorkspaceLiveProvider><WorkspaceLiveUpdates brandId="brand-1" /><DraftProbe /></WorkspaceLiveProvider>);
  act(() => latest().failClosed());
  await act(async () => { await vi.advanceTimersByTimeAsync(1000); });
  expect(router.replace).not.toHaveBeenCalled();
  expect(navigation.navigateTo).not.toHaveBeenCalled();
  expect(MockEventSource.instances.length).toBeGreaterThan(1);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

test("opens one stream per workspace and refreshes once when change events arrive", async () => {
  vi.useFakeTimers();
  renderLive();

  expect(MockEventSource.instances).toHaveLength(1);
  expect(latest().url).toBe("/api/brands/brand-1/live");

  for (const type of WORKSPACE_LIVE_EVENTS) {
    latest().emit(type);
  }
  latest().emit("reset");

  await vi.advanceTimersByTimeAsync(100);
  expect(router.refresh).toHaveBeenCalledOnce();
});

test("refreshes for brand kit changes", async () => {
  vi.useFakeTimers();
  renderLive();

  latest().emit("brand_kit.changed");
  await vi.advanceTimersByTimeAsync(100);
  latest().emit("brand_kit.files_changed");
  await vi.advanceTimersByTimeAsync(100);

  expect(router.refresh).toHaveBeenCalledTimes(2);
});

test("agent progress updates the screen without refreshing the page", async () => {
  vi.useFakeTimers();
  renderLive();

  act(() => latest().emit("agent.progress", progress()));
  expect(screen.getByText("create: running Generating video")).toBeTruthy();

  act(() => latest().emit("agent.progress", progress({ status: "succeeded", step: "Assets ready", at: "2026-10-14T09:06:00Z" })));
  expect(screen.getByText("create: succeeded Assets ready")).toBeTruthy();
  expect(screen.queryByText(/Generating video/)).toBeNull();

  act(() => latest().emit("agent.progress", "not json"));
  await vi.advanceTimersByTimeAsync(1_000);
  expect(router.refresh).not.toHaveBeenCalled();
});

test("reports the connection state", () => {
  renderLive();
  expect(screen.getByTestId("connection").textContent).toBe("connecting");

  act(() => latest().open());
  expect(screen.getByTestId("connection").textContent).toBe("open");

  act(() => latest().failConnecting());
  expect(screen.getByTestId("connection").textContent).toBe("reconnecting");
  expect(MockEventSource.instances).toHaveLength(1);

  act(() => latest().open());
  expect(screen.getByTestId("connection").textContent).toBe("open");
  expect(router.refresh).not.toHaveBeenCalled();
});

test("reopens a closed stream with growing backoff and catches up once it is back", async () => {
  vi.useFakeTimers();
  renderLive();
  latest().open();

  await act(async () => latest().failClosed());
  expect(screen.getByTestId("connection").textContent).toBe("reconnecting");
  await vi.advanceTimersByTimeAsync(999);
  expect(MockEventSource.instances).toHaveLength(1);
  await vi.advanceTimersByTimeAsync(1);
  expect(MockEventSource.instances).toHaveLength(2);

  await act(async () => latest().failClosed());
  await vi.advanceTimersByTimeAsync(1_999);
  expect(MockEventSource.instances).toHaveLength(2);
  await vi.advanceTimersByTimeAsync(1);
  expect(MockEventSource.instances).toHaveLength(3);
  expect(router.refresh).not.toHaveBeenCalled();

  act(() => latest().open());
  await vi.advanceTimersByTimeAsync(100);
  expect(router.refresh).toHaveBeenCalledOnce();
  expect(screen.getByTestId("connection").textContent).toBe("open");

  await act(async () => latest().failClosed());
  await vi.advanceTimersByTimeAsync(1_000);
  expect(MockEventSource.instances).toHaveLength(4);
  expect(fetch).toHaveBeenCalledWith("/api/brands/brand-1", { credentials: "same-origin", cache: "no-store" });
});

test("keeps retrying when the access check cannot reach the server", async () => {
  vi.useFakeTimers();
  vi.mocked(fetch).mockRejectedValue(new TypeError("Failed to fetch"));
  renderLive();

  await act(async () => latest().failClosed());
  await vi.advanceTimersByTimeAsync(1_000);

  expect(MockEventSource.instances).toHaveLength(2);
  expect(router.replace).not.toHaveBeenCalled();
  expect(navigation.navigateTo).not.toHaveBeenCalled();
});

test("does not reconnect after unmounting", async () => {
  vi.useFakeTimers();
  const { unmount } = renderLive();
  const first = latest();

  await act(async () => first.failClosed());
  unmount();
  await vi.advanceTimersByTimeAsync(RECONNECT_MAX_DELAY_MS * 2);

  expect(MockEventSource.instances).toHaveLength(1);
});

test("does not reconnect when unmounted while checking access", async () => {
  vi.useFakeTimers();
  let answer: (response: Response) => void = () => {};
  vi.mocked(fetch).mockReturnValue(new Promise((resolve) => (answer = resolve)));
  const { unmount } = renderLive();

  latest().failClosed();
  unmount();
  await act(async () => answer(new Response(null, { status: 404 })));
  await vi.advanceTimersByTimeAsync(RECONNECT_MAX_DELAY_MS * 2);

  expect(MockEventSource.instances).toHaveLength(1);
  expect(navigation.navigateTo).not.toHaveBeenCalled();
});

test("sends a signed-out user to sign-in", async () => {
  vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 401 }));
  vi.useFakeTimers();
  renderLive();

  await act(async () => latest().failClosed());
  await vi.advanceTimersByTimeAsync(RECONNECT_MAX_DELAY_MS * 2);

  expect(router.replace).toHaveBeenCalledWith("/sign-in?returnUrl=%2Facme%2Ftoday");
  expect(navigation.navigateTo).not.toHaveBeenCalled();
  expect(MockEventSource.instances).toHaveLength(1);
});

test("takes someone who left the workspace out of it", async () => {
  vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 404 }));
  vi.useFakeTimers();
  renderLive();

  await act(async () => latest().failClosed());
  await vi.advanceTimersByTimeAsync(RECONNECT_MAX_DELAY_MS * 2);

  expect(navigation.navigateTo).toHaveBeenCalledWith("/");
  expect(router.replace).not.toHaveBeenCalled();
  expect(MockEventSource.instances).toHaveLength(1);
});

test("closes the stream and forgets agent jobs when the workspace changes", () => {
  const { switchTo } = renderLive();
  const first = latest();
  act(() => first.emit("agent.progress", progress()));
  expect(screen.getByText(/Generating video/)).toBeTruthy();

  switchTo("brand-2");

  expect(first.readyState).toBe(CLOSED);
  expect(MockEventSource.instances).toHaveLength(2);
  expect(latest().url).toBe("/api/brands/brand-2/live");
  expect(screen.queryByText(/Generating video/)).toBeNull();
});

test("backoff is capped and jittered", () => {
  expect(reconnectDelay(0, () => 0)).toBe(500);
  expect(reconnectDelay(0, () => 1)).toBe(1_000);
  expect(reconnectDelay(3, () => 1)).toBe(8_000);
  expect(reconnectDelay(20, () => 1)).toBe(RECONNECT_MAX_DELAY_MS);
  expect(reconnectDelay(20, () => 0)).toBe(RECONNECT_MAX_DELAY_MS / 2);
});
