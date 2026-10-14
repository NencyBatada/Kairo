import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { AuditLogFilters } from "./audit-log-filters";

const actions = [
  { action: "member.removed", label: "Removed a member" },
  { action: "brand_kit.approved", label: "Approved the Brand Kit" },
  { action: "analytics.sales_uploaded", label: "Uploaded sales data" },
];

test("offers every action from core, actor, actor type and date range filters", () => {
  render(
    <AuditLogFilters
      workspace="acme"
      brandId="b1"
      actions={actions}
      search={{ actions: ["brand_kit.approved"], actor: "Neha", actorType: "agent", from: "2026-10-01", to: "2026-10-14" }}
    />,
  );

  const what = screen.getByLabelText("What") as HTMLSelectElement;
  expect(within(what).getAllByRole("option").map((option) => option.textContent)).toEqual([
    "All actions",
    "Removed a member",
    "Approved the Brand Kit",
    "Uploaded sales data",
  ]);
  expect(what.value).toBe("brand_kit.approved");
  expect((screen.getByLabelText("Who") as HTMLInputElement).value).toBe("Neha");
  expect((screen.getByLabelText("Done by") as HTMLSelectElement).value).toBe("agent");
  expect((screen.getByLabelText("From") as HTMLInputElement).value).toBe("2026-10-01");
  expect((screen.getByLabelText("To") as HTMLInputElement).type).toBe("date");
  expect(screen.getByRole("link", { name: "Clear filters" }).getAttribute("href")).toBe("/acme/settings/audit-log");
});

test("exports through the core API with the same filters as a plain download link", () => {
  render(<AuditLogFilters workspace="acme" brandId="b1" actions={actions} search={{ actions: ["member.removed"], actor: "Neha Rao", to: "2026-10-14" }} />);

  const link = screen.getByRole("link", { name: "Export CSV" });
  expect(link.getAttribute("href")).toBe("/api/brands/b1/audit-log/export?action=member.removed&actor=Neha+Rao&to=2026-10-14");
  expect(link.hasAttribute("download")).toBe(true);
});

test("hides clear filters when nothing is filtered", () => {
  render(<AuditLogFilters workspace="acme" brandId="b1" actions={actions} search={{ actions: [] }} />);

  expect(screen.queryByRole("link", { name: "Clear filters" })).toBeNull();
  expect(screen.getByRole("link", { name: "Export CSV" }).getAttribute("href")).toBe("/api/brands/b1/audit-log/export");
});
