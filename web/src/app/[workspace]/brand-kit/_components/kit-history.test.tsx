import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { KitHistoryList } from "./kit-history";

test("shows historical versions with status, local approval date and review links", () => {
  render(<KitHistoryList handle="acme" timeZone="Asia/Kolkata" versions={[{ number: 1, status: "superseded", origin: "manual", restoredFrom: null, createdBy: "admin", updatedBy: "admin", createdAt: "2026-10-14T20:00:00Z", updatedAt: "2026-10-14T20:00:00Z", approvedAt: "2026-10-14T20:00:00Z", approvedBy: "admin" }]} />);
  expect(screen.getByRole("link", { name: /Version 1/ }).getAttribute("href")).toBe("/acme/brand-kit/versions/1");
  expect(screen.getAllByText(/15 Oct 2026/)).toHaveLength(2);
  expect(screen.getByText("Superseded")).toBeTruthy();
});
