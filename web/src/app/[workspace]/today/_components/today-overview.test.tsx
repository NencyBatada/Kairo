import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { TodayOverview } from "./today-overview";

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
  render(<TodayOverview items={[notification]} />);

  expect(screen.getByRole("heading", { level: 1, name: "Today" })).toBeTruthy();
  expect(screen.getByRole("heading", { name: "Recent updates" })).toBeTruthy();
  expect(screen.getByText(notification.title)).toBeTruthy();
  expect(screen.getByText(notification.body)).toBeTruthy();
  expect(screen.getByText("New")).toBeTruthy();
  expect(screen.getByText("14 Oct 2026").getAttribute("datetime")).toBe(notification.createdAt);
  expect(screen.queryByText(/0 approvals waiting/i)).toBeNull();
  expect(screen.queryByRole("link", { name: /reconnect/i })).toBeNull();
});

test("explains unavailable feeds and has a distinct empty updates state", () => {
  render(<TodayOverview items={[]} />);

  for (const title of ["No recent updates", "Approvals", "Top opportunities", "Live tests"]) {
    expect(screen.getByRole("heading", { name: title })).toBeTruthy();
  }
  expect(screen.getByText(/when campaign approvals are available/i)).toBeTruthy();
  expect(screen.getByText(/when Opportunity Radar is available/i)).toBeTruthy();
  expect(screen.getByText(/when campaigns are live/i)).toBeTruthy();
});
