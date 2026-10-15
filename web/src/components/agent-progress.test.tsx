import { act, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import type { AgentJobProgress } from "@/lib/agent-progress";

import { AgentProgress, AgentProgressList } from "./agent-progress";
import { LiveConnectionIndicator, RECONNECTING_NOTICE_DELAY_MS } from "./live-connection-indicator";
import { type LiveDispatch, useLiveDispatch, WorkspaceLiveProvider } from "./workspace-live-provider";

const running: AgentJobProgress = {
  jobId: "job-1",
  agent: "create",
  status: "running",
  step: "Generating video",
  stepNumber: 2,
  stepCount: 5,
  message: "Rendering the 15-second cut",
  subject: { kind: "campaign", id: "campaign-1" },
  at: "2026-10-14T09:05:00Z",
};

afterEach(() => {
  vi.useRealTimers();
});

test("shows the agent, its step, the step count and a progress bar", () => {
  render(<AgentProgress job={running} />);

  expect(screen.getByText("Create agent")).toBeTruthy();
  expect(screen.getByText("Running")).toBeTruthy();
  expect(screen.getByText("Generating video")).toBeTruthy();
  expect(screen.getByText(/Step 2 of 5/)).toBeTruthy();
  expect(screen.getByText("Rendering the 15-second cut")).toBeTruthy();
  const bar = screen.getByRole("progressbar", { name: "Create agent progress" });
  expect(bar.getAttribute("aria-valuenow")).toBe("2");
  expect(bar.getAttribute("aria-valuemax")).toBe("5");
  expect(bar.getAttribute("aria-valuetext")).toBe("Step 2 of 5");
});

test("shows finished and failed jobs in words, without a bar when there are no steps", () => {
  const { rerender } = render(<AgentProgress job={{ ...running, status: "succeeded", stepNumber: null, stepCount: null, message: null }} />);
  expect(screen.getByText("Done")).toBeTruthy();
  expect(screen.queryByRole("progressbar")).toBeNull();

  rerender(<AgentProgress job={{ ...running, status: "failed" }} />);
  expect(screen.getByText("Failed")).toBeTruthy();
});

test("announces job changes politely", () => {
  render(<AgentProgressList jobs={[running]} label="Agent jobs" />);

  const list = screen.getByRole("list", { name: "Agent jobs" });
  expect(list.getAttribute("aria-live")).toBe("polite");
  expect(screen.getAllByRole("listitem")).toHaveLength(1);
});

test("says it is reconnecting only after a short delay, and clears once the stream is back", () => {
  vi.useFakeTimers();
  let dispatch: LiveDispatch | null = null;
  function Capture() {
    dispatch = useLiveDispatch();
    return null;
  }
  render(
    <WorkspaceLiveProvider>
      <Capture />
      <LiveConnectionIndicator />
    </WorkspaceLiveProvider>,
  );
  const status = screen.getByRole("status");
  expect(status.textContent).toBe("");

  act(() => dispatch!.setConnection("reconnecting"));
  expect(status.textContent).toBe("");
  act(() => vi.advanceTimersByTime(RECONNECTING_NOTICE_DELAY_MS));
  expect(status.textContent).toBe("Reconnecting to live updates…");

  act(() => dispatch!.setConnection("open"));
  expect(status.textContent).toBe("");
});

test("stays quiet outside a workspace", () => {
  render(<LiveConnectionIndicator />);

  expect(screen.getByRole("status").textContent).toBe("");
});
