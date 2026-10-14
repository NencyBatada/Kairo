import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useEffect } from "react";
import { expect, test, vi } from "vitest";

import { useLiveDispatch, WorkspaceLiveProvider } from "@/components/workspace-live-provider";

import { TodayOverview } from "./today-overview";

const { markNotificationRead } = vi.hoisted(() => ({ markNotificationRead: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("../../settings/notifications/actions", () => ({ markNotificationRead }));

const notification = {
  id: 1,
  kind: "connection.problem",
  title: "Google Ads needs reconnecting",
  body: "Reconnect Google Ads in Settings.",
  link: "/settings/connections",
  createdAt: "2026-10-14T09:30:00Z",
  readAt: null,
};

test("shows real recent updates without pretending they are pending approvals", () => {
  render(<TodayOverview items={[notification]} workspace="acme" brandId="brand-1" timeZone="Asia/Kolkata" />);

  expect(screen.getByRole("heading", { level: 1, name: "Today" })).toBeTruthy();
  expect(screen.getByRole("heading", { name: "Recent updates" })).toBeTruthy();
  expect(screen.getByText(notification.title)).toBeTruthy();
  expect(screen.getByText(notification.body)).toBeTruthy();
  expect(screen.getByText("New")).toBeTruthy();
  expect(screen.getByText("14 Oct 2026, 15:00").getAttribute("datetime")).toBe(notification.createdAt);
  expect(screen.queryByText(/0 approvals waiting/i)).toBeNull();
  expect(screen.getByRole("link", { name: new RegExp(notification.title) }).getAttribute("href")).toBe("/acme/settings/connections");
});

test("explains unavailable feeds and has a distinct empty updates state", () => {
  render(<TodayOverview items={[]} workspace="acme" brandId="brand-1" timeZone="Asia/Kolkata" />);

  for (const title of ["No recent updates", "Approvals", "Top opportunities", "Live tests"]) {
    expect(screen.getByRole("heading", { name: title })).toBeTruthy();
  }
  expect(screen.getByText(/when campaign approvals are available/i)).toBeTruthy();
  expect(screen.getByText(/when Opportunity Radar is available/i)).toBeTruthy();
  expect(screen.getByText(/when campaigns are live/i)).toBeTruthy();
});

test("shows times in the workspace time zone rather than UTC", () => {
  render(
    <TodayOverview items={[{ ...notification, createdAt: "2026-10-14T20:00:00Z" }]} workspace="acme" brandId="brand-1" timeZone="Asia/Kolkata" />,
  );

  expect(screen.getByText("15 Oct 2026, 01:30")).toBeTruthy();
});

test("marks an unread update read when it is opened from Today", async () => {
  markNotificationRead.mockResolvedValue({ ok: true });
  render(<TodayOverview items={[notification]} workspace="acme" brandId="brand-1" timeZone="Asia/Kolkata" />);

  fireEvent.click(screen.getByRole("link", { name: new RegExp(notification.title) }));

  await waitFor(() => expect(markNotificationRead).toHaveBeenCalledWith("brand-1", 1));
});

test("shows agent jobs while they run", () => {
  function Report() {
    const { reportProgress } = useLiveDispatch();
    useEffect(() => {
      reportProgress({
        jobId: "job-1",
        agent: "sense",
        status: "running",
        step: "Scoring opportunities",
        stepNumber: 1,
        stepCount: 3,
        message: null,
        subject: null,
        at: "2026-10-14T09:05:00Z",
      });
    }, [reportProgress]);
    return null;
  }

  render(
    <WorkspaceLiveProvider>
      <Report />
      <TodayOverview items={[]} workspace="acme" brandId="brand-1" timeZone="Asia/Kolkata" />
    </WorkspaceLiveProvider>,
  );

  expect(screen.getByRole("heading", { name: "Agents at work" })).toBeTruthy();
  expect(screen.getByText("1 job running")).toBeTruthy();
  expect(screen.getByText("Sense agent")).toBeTruthy();
  expect(screen.getByText("Scoring opportunities")).toBeTruthy();
});

test("says when no agents are working", () => {
  render(<TodayOverview items={[]} workspace="acme" brandId="brand-1" timeZone="Asia/Kolkata" />);

  expect(screen.getByRole("heading", { name: "Agents at work" })).toBeTruthy();
  expect(screen.getByText(/No agents are working right now/)).toBeTruthy();
});
