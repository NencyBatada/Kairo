import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { CampaignRoomTabs } from "./campaign-room-tabs";

function tabLinks() {
  const nav = screen.getByRole("navigation", { name: "Campaign room" });
  return within(nav)
    .getAllByRole("link")
    .map((link) => [link.textContent, link.getAttribute("href")]);
}

test("links to the six tabs of the room inside its workspace", () => {
  render(<CampaignRoomTabs workspace="acme" roomId="42" />);

  expect(tabLinks()).toEqual([
    ["Evidence", "/acme/campaigns/42/evidence"],
    ["Brief", "/acme/campaigns/42/brief"],
    ["Assets", "/acme/campaigns/42/assets"],
    ["Launch", "/acme/campaigns/42/launch"],
    ["Performance", "/acme/campaigns/42/performance"],
    ["Results", "/acme/campaigns/42/results"],
  ]);
});

test("encodes room ids that contain URL characters", () => {
  render(<CampaignRoomTabs workspace="acme" roomId="a/b?c" />);

  expect(tabLinks()[0]).toEqual(["Evidence", "/acme/campaigns/a%2Fb%3Fc/evidence"]);
});
