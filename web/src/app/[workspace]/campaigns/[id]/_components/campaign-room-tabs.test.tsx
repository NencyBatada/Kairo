import { render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { CampaignRoomTabs } from "./campaign-room-tabs";

const route = vi.hoisted(() => ({ pathname: null as string | null }));
vi.mock("next/navigation", () => ({ usePathname: () => route.pathname }));
beforeEach(() => { route.pathname = null; });

function tabLinks() {
  const nav = screen.getByRole("navigation", { name: "Campaign room" });
  return within(nav)
    .getAllByRole("link")
    .map((link) => [link.textContent, link.getAttribute("href")]);
}

test("links to overview and the stage tabs inside its workspace", () => {
  render(<CampaignRoomTabs workspace="acme" roomId="42" />);

  expect(tabLinks()).toEqual([
    ["Overview", "/acme/campaigns/42"],
    ["Evidence", "/acme/campaigns/42/evidence"],
    ["Brief", "/acme/campaigns/42/brief"],
    ["Assets", "/acme/campaigns/42/assets"],
    ["Brand Guardian", "/acme/campaigns/42/review"],
    ["Launch", "/acme/campaigns/42/launch"],
    ["Performance", "/acme/campaigns/42/performance"],
    ["Results", "/acme/campaigns/42/results"],
  ]);
});

test("encodes room ids that contain URL characters", () => {
  render(<CampaignRoomTabs workspace="acme" roomId="a/b?c" />);

  expect(tabLinks()[0]).toEqual(["Overview", "/acme/campaigns/a%2Fb%3Fc"]);
});

test("marks only the selected stage tab as current and keeps Overview exact", () => {
  route.pathname = "/acme/campaigns/42/evidence";
  render(<CampaignRoomTabs workspace="acme" roomId="42" />);
  expect(screen.getByRole("link", { name: "Overview" }).getAttribute("aria-current")).toBeNull();
  expect(screen.getByRole("link", { name: "Evidence" }).getAttribute("aria-current")).toBe("page");
});
