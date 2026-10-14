import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import type { AuditEntry } from "@/lib/core-api/types";

import { AuditLogTable } from "./audit-log-table";

const entry: AuditEntry = {
  id: 7,
  occurredAt: "2026-10-13T04:30:00Z",
  actorType: "user",
  actorId: "u1",
  actorName: "Neha Rao",
  actorEmail: "neha@example.com",
  action: "brand.created",
  targetType: "brand",
  targetId: "b1",
  before: null,
  after: null,
};

const labels = { "brand.created": "Created the workspace", "brand_kit.approved": "Approved the Brand Kit" };

test("lists who did what to which target and when, in the workspace's time zone", () => {
  render(<AuditLogTable page={{ entries: [entry], nextBefore: null }} timeZone="Asia/Kolkata" actionLabels={labels} olderHref={(before) => `?before=${before}`} />);

  expect(screen.getByText("Neha Rao").getAttribute("title")).toBe("neha@example.com");
  expect(screen.getByText("Person")).toBeTruthy();
  expect(screen.getByText("Created the workspace")).toBeTruthy();
  expect(screen.getByText("Workspace")).toBeTruthy();
  expect(screen.getByText("b1")).toBeTruthy();
  expect(screen.getByText(/10:00/)).toBeTruthy();
  expect(screen.queryByRole("link", { name: "Older entries" })).toBeNull();
});

test("shows agents as agents", () => {
  render(
    <AuditLogTable
      page={{ entries: [{ ...entry, actorType: "agent", actorId: "sense", actorName: "Sense agent", actorEmail: null, action: "brand_kit.approved", targetType: "brand_kit_version", targetId: "3" }], nextBefore: null }}
      timeZone="UTC"
      actionLabels={labels}
      olderHref={(before) => before}
    />,
  );

  expect(screen.getByText("Sense agent")).toBeTruthy();
  expect(screen.getByText("Agent")).toBeTruthy();
  expect(screen.getByText("Approved the Brand Kit")).toBeTruthy();
  expect(screen.getByText("Brand Kit version")).toBeTruthy();
});

test("links to older entries when there are more", () => {
  render(
    <AuditLogTable
      page={{ entries: [entry], nextBefore: "7" }}
      timeZone="Asia/Kolkata"
      actionLabels={labels}
      olderHref={(before) => `/acme/settings/audit-log?action=member.removed&before=${before}`}
    />,
  );

  expect(screen.getByRole("link", { name: "Older entries" }).getAttribute("href")).toBe("/acme/settings/audit-log?action=member.removed&before=7");
});

test("shows unknown actions and targets as they are", () => {
  render(
    <AuditLogTable
      page={{ entries: [{ ...entry, action: "launch.started", targetType: "launch_plan" }], nextBefore: null }}
      timeZone="UTC"
      actionLabels={labels}
      olderHref={(before) => before}
    />,
  );

  expect(screen.getByText("launch.started")).toBeTruthy();
  expect(screen.getByText("launch plan")).toBeTruthy();
});

test("explains when filters match nothing", () => {
  render(<AuditLogTable page={{ entries: [], nextBefore: null }} timeZone="UTC" actionLabels={labels} olderHref={() => "/"} filtered />);

  expect(screen.getByRole("heading", { name: "No matching entries" })).toBeTruthy();
});
