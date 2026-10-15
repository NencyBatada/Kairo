import { act, fireEvent, render, screen } from "@testing-library/react";
import { useEffect } from "react";
import { expect, test, vi } from "vitest";

import { WorkspaceLiveProvider, useLiveDispatch } from "@/components/workspace-live-provider";
import type { AgentJobProgress } from "@/lib/agent-progress";
import type { FormState } from "@/lib/forms";

import { run } from "../fixtures.test-data";
import { RoomRuns } from "./room-runs";

const progress: AgentJobProgress = { jobId: "run-1", agent: "plan", status: "running", step: "Drafting the starter brief", stepNumber: 1, stepCount: 2, message: null, subject: { kind: "campaign", id: "room-1" }, at: "2026-10-15T04:00:00Z" };
function Feed() {
  const { reportProgress } = useLiveDispatch();
  useEffect(() => { reportProgress(progress); reportProgress({ ...progress, jobId: "other-run", step: "Another campaign", subject: { kind: "campaign", id: "other-room" } }); }, [reportProgress]);
  return null;
}

test("shows live progress only for this campaign alongside persisted runs", () => {
  render(<WorkspaceLiveProvider><Feed /><RoomRuns roomId="room-1" runs={[run]} retryId={null} retry={vi.fn()} timeZone="Asia/Kolkata" /></WorkspaceLiveProvider>);
  expect(screen.getByText("Drafting the starter brief")).toBeTruthy();
  expect(screen.queryByText("Another campaign")).toBeNull();
  expect(screen.getByText(run.error!)).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Try agent again" })).toBeNull();
});

test("retry stays pending, shows errors inline and can be tried again", async () => {
  let finish: (state: FormState) => void = () => {};
  const retry = vi.fn(() => new Promise<FormState>((resolve) => { finish = resolve; }));
  render(<RoomRuns roomId="room-1" runs={[run]} retryId="run-1" retry={retry} timeZone="Asia/Kolkata" />);
  fireEvent.click(screen.getByRole("button", { name: "Try agent again" }));
  expect((screen.getByRole("button", { name: "Try agent again" }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => { finish({ status: "error", message: "The room has moved on. Reload it." }); });
  expect(screen.getByRole("alert").textContent).toContain("The room has moved on.");
  expect((screen.getByRole("button", { name: "Try agent again" }) as HTMLButtonElement).disabled).toBe(false);
  expect(retry).toHaveBeenCalledOnce();
});

test("an empty run history explains when work will appear", () => {
  render(<RoomRuns roomId="room-1" runs={[]} retryId={null} retry={vi.fn()} timeZone="Asia/Kolkata" />);
  expect(screen.getByText(/Agent work will appear/)).toBeTruthy();
});
