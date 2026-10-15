import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { trigger, brief } from "./triggers.test-data";
import { opportunity } from "./fixtures.test-data";
import { OpportunityCard } from "./opportunity-card";
import { TriggerSummary } from "./trigger-summary";
import { TriggerEvidence } from "./trigger-evidence";

test.each([["weather", "Weather trigger"], ["festival", "Festival trigger"], ["stock", "Stock trigger"]])("labels a %s trigger with its condition and regions", (source, label) => {
  render(<TriggerSummary trigger={{ ...trigger, source }} />);
  expect(screen.getByText(label)).toBeTruthy(); expect(screen.getByText(trigger.summary)).toBeTruthy(); expect(screen.getByText("Mumbai")).toBeTruthy();
});
test("the shared card carries the trigger and keeps Radar navigation", () => {
  render(<OpportunityCard item={{ ...opportunity, trigger }} handle="acme" rank={1} />);
  expect(screen.getByText("Weather trigger")).toBeTruthy(); expect(screen.getByText(trigger.summary)).toBeTruthy();
  expect(screen.getByRole("link", { name: opportunity.topic }).getAttribute("href")).toBe(`/acme/opportunities/${opportunity.id}`);
});
test("detail shows all merged triggers, local times, rule links and the brief", () => {
  render(<TriggerEvidence triggers={[trigger, { ...trigger, firingId: "firing-2", ruleId: "rule-2", ruleName: "Overstock", source: "stock", ended: true }]} brief={brief} briefStatus="drafted" handle="acme" timeZone="Asia/Kolkata" />);
  expect(screen.getByRole("link", { name: "View rule: Rain in Mumbai" }).getAttribute("href")).toBe("/acme/triggers/rule-1");
  expect(screen.getAllByText(/Fired 15 Oct 2026, 6:30/)).toHaveLength(2);
  expect(screen.getByText("Ready for rain")).toBeTruthy(); expect(screen.getByText(brief.keyMessage)).toBeTruthy();
  expect(screen.getByText(/Drafted by Kairo/)).toBeTruthy(); expect(screen.getByText(/still need approval before any money is spent/)).toBeTruthy();
  expect(screen.getAllByText(/Condition ended/).length).toBeGreaterThan(0);
});
test.each([["failed", "The brief could not be drafted."], [null, "The suggested brief is not available yet."]])("shows an honest brief fallback for %s", (status, message) => {
  render(<TriggerEvidence triggers={[trigger]} brief={null} briefStatus={status} handle="acme" timeZone="Asia/Kolkata" />);
  expect(screen.getByText(new RegExp(message))).toBeTruthy(); expect(screen.queryByText(/Drafted by Kairo/)).toBeNull();
});
