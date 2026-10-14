import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { AuditLogFilters } from "./audit-log-filters";

test("offers action and actor filters plus export", () => {
  render(<AuditLogFilters workspace="acme" action="member.removed" actor="Neha" />);

  expect(screen.getByLabelText("What")).toBeTruthy();
  expect((screen.getByLabelText("Who") as HTMLInputElement).value).toBe("Neha");
  expect(screen.getByRole("link", { name: "Export CSV" }).getAttribute("href")).toBe(
    "/acme/settings/audit-log/export?action=member.removed&actor=Neha",
  );
  expect(screen.getByRole("link", { name: "Clear filters" })).toBeTruthy();
});
