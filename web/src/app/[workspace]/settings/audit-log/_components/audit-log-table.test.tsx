import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { AuditLogTable } from "./audit-log-table";

const entry = {
  id: 7,
  occurredAt: "2026-10-13T04:30:00Z",
  actorType: "user",
  actorId: "u1",
  actorName: "Neha Rao",
  action: "brand.created",
  targetType: "brand",
  targetId: "b1",
  before: null,
  after: null,
};

test("lists who did what and when, in the workspace's time zone", () => {
  render(<AuditLogTable page={{ entries: [entry], nextBefore: null }} timeZone="Asia/Kolkata" olderHref={(before) => `?before=${before}`} />);

  expect(screen.getByText("Neha Rao")).toBeTruthy();
  expect(screen.getByText("Created the workspace")).toBeTruthy();
  expect(screen.getByText(/10:00/)).toBeTruthy();
  expect(screen.queryByRole("link", { name: "Older entries" })).toBeNull();
});

test("links to older entries when there are more", () => {
  render(<AuditLogTable page={{ entries: [entry], nextBefore: "7" }} timeZone="Asia/Kolkata" olderHref={(before) => `/acme/settings/audit-log?before=${before}`} />);

  expect(screen.getByRole("link", { name: "Older entries" }).getAttribute("href")).toBe("/acme/settings/audit-log?before=7");
});

test("shows unknown actions as they are", () => {
  render(<AuditLogTable page={{ entries: [{ ...entry, action: "launch.started" }], nextBefore: null }} timeZone="UTC" olderHref={(before) => before} />);

  expect(screen.getByText("launch.started")).toBeTruthy();
});

test("explains when filters hide every entry on the page", () => {
  render(<AuditLogTable page={{ entries: [], nextBefore: null }} timeZone="UTC" olderHref={() => "/"} filtered />);

  expect(screen.getByRole("heading", { name: "No matching entries" })).toBeTruthy();
});
