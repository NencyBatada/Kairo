import { act, render, screen, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { room } from "../fixtures.test-data";
import { RoomTracker } from "./room-tracker";

const now = Date.parse("2026-10-15T04:00:00Z");
const show = (value = room) => render(<RoomTracker room={value} handle="acme" timeZone="Asia/Kolkata" now={now} />);
afterEach(() => vi.useRealTimers());

test("shows the current stage, owner, next action and review link", () => {
  show();
  expect(screen.getByRole("status").textContent).toContain("Needs attention");
  const stages = screen.getByRole("list", { name: "Campaign stages" });
  expect(within(stages).getAllByRole("listitem")).toHaveLength(8);
  expect(stages.querySelector('[aria-current="step"]')?.textContent).toContain("Brief draft");
  expect(screen.getByText(room.tracker.nextAction)).toBeTruthy();
  expect(screen.getByRole("link", { name: "Open brief" }).getAttribute("href")).toBe("/acme/campaigns/room-1/brief");
  expect(screen.getByText("30 minutes")).toBeTruthy();
});

test("updates elapsed stage time and cleans up the timer", () => {
  vi.useFakeTimers(); vi.setSystemTime(now);
  const view = show();
  act(() => vi.advanceTimersByTime(60_000));
  expect(screen.getByText("31 minutes")).toBeTruthy();
  view.unmount(); expect(vi.getTimerCount()).toBe(0);
});

test("names every pending approver and preserves send-back reasons", () => {
  show({ ...room, status: "waiting_approval", pendingApproval: { gate: "brief", label: "Gate 1 · Brief", approvers: [{ id: "p1", name: "Meera Rao" }, { id: "p2", name: "Neha Shah" }], requestedBy: room.owner, requestedAt: room.updatedAt }, lastSendBack: { from: "asset_review", to: "brief_draft", comment: "Use the rainy-season products.", by: { id: "p1", name: "Meera Rao" }, at: room.updatedAt } });
  expect(screen.getByText("Approvers: Meera Rao, Neha Shah.")).toBeTruthy();
  expect(screen.getByText("Use the rainy-season products.")).toBeTruthy();
  expect(screen.queryByRole("button", { name: /Approve/ })).toBeNull();
});

test("explains a missing approver and agent ownership", () => {
  show({ ...room, tracker: { ...room.tracker, ownerKind: "agent", owner: "Plan agent" }, pendingApproval: { gate: "brief", label: "Gate 1 · Brief", approvers: [], requestedBy: null, requestedAt: room.updatedAt } });
  expect(screen.getByText("Working now")).toBeTruthy();
  expect(screen.getByText(/No approver is assigned/)).toBeTruthy();
});

test.each(["closed", "discarded"])("a %s room has a final timestamp and no next-step link", (stage) => {
  show({ ...room, stage, status: stage, tracker: { ...room.tracker, owner: "", ownerKind: "none" }, discardReason: stage === "discarded" ? "Demand passed." : null });
  expect(screen.getByText("Final stage entered")).toBeTruthy();
  expect(screen.getByText("No action needed")).toBeTruthy();
  expect(screen.queryByRole("link", { name: /Open/ })).toBeNull();
  expect(screen.getByRole("list", { name: "Campaign stages" }).querySelector('[aria-current="step"]')).toBeNull();
  if (stage === "discarded") expect(screen.getByText("Demand passed.")).toBeTruthy();
});
