import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { room } from "../fixtures.test-data";
import Page from "../evidence/page";
import { activityFacts, evidenceSnapshot } from "./evidence";

const { getRoom } = vi.hoisted(() => ({ getRoom: vi.fn() }));
vi.mock("./room", () => ({ getRoom }));
const params = Promise.resolve({ workspace: "acme", id: "room-1" });
beforeEach(() => getRoom.mockResolvedValue({ room, timeZone: "Asia/Kolkata" }));

test("renders saved evidence and links to the original opportunity", async () => {
  render(await Page({ params }));
  expect(screen.getByText("Maharashtra")).toBeTruthy(); expect(screen.getByText("Rain Runner")).toBeTruthy();
  expect(screen.getByRole("link", { name: "Open original opportunity" }).getAttribute("href")).toBe("/acme/opportunities/opportunity-1");
  const source = screen.getByRole("link", { name: /Weather report/ });
  expect(source.getAttribute("href")).toBe("https://example.com/weather"); expect(source.getAttribute("rel")).toContain("noopener");
});

test("handles malformed evidence and blocks unsafe source URLs", async () => {
  expect(evidenceSnapshot(null)).toMatchObject({ products: [], reasons: [] });
  const evidence = { regions: [null, { name: 2 }], products: "bad", reasons: [{ text: "Rising demand", sources: [{ title: "Unsafe", url: "javascript:alert(1)" }] }] };
  getRoom.mockResolvedValue({ room: { ...room, evidence }, timeZone: "Asia/Kolkata" });
  render(await Page({ params }));
  expect(screen.getByText("No product names were captured.")).toBeTruthy(); expect(screen.getByText(/No source supports/)).toBeTruthy();
  expect(screen.queryByRole("link", { name: /Unsafe/ })).toBeNull();
});

test("activity details preserve comments and present stage changes without internal metadata", () => {
  expect(activityFacts({ comment: "Use rain_runner.", from: "asset_review", to: "brief_draft", version: 2, runId: "secret-id", nested: { token: "value" } })).toEqual([{ label: "Comment", value: "Use rain_runner." }, { label: "From", value: "asset review" }, { label: "To", value: "brief draft" }]);
  expect(activityFacts(null)).toEqual([]);
  expect(activityFacts({ state: "brief_draft", details: { comment: "Use more Hindi.", from: "asset_review" } })).toEqual([{ label: "Stage", value: "brief draft" }, { label: "Comment", value: "Use more Hindi." }, { label: "From", value: "asset review" }]);
});
