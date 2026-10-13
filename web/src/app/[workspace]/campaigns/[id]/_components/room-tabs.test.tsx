import { render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { RoomTabs } from "./room-tabs";

vi.mock("next/navigation", () => ({ usePathname: () => null }));

async function evidenceHref(id: string) {
  render(await RoomTabs({ params: Promise.resolve({ workspace: "acme", id }) }));
  const nav = screen.getByRole("navigation", { name: "Campaign room" });
  return within(nav).getByRole("link", { name: "Evidence" }).getAttribute("href");
}

test("keeps a room id that arrives percent-encoded from the URL", async () => {
  expect(await evidenceHref("a%2Fb%3Fc")).toBe("/acme/campaigns/a%2Fb%3Fc/evidence");
});

test("keeps an encoded space in a room id", async () => {
  expect(await evidenceHref("hello%20world")).toBe("/acme/campaigns/hello%20world/evidence");
});

test("keeps a room id that is not valid percent-encoding", async () => {
  expect(await evidenceHref("100%")).toBe("/acme/campaigns/100%25/evidence");
});
