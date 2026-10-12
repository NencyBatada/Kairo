import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { CampaignRoomTabs } from "./campaign-room-tabs";

function tabLinks() {
  const nav = screen.getByRole("navigation", { name: "Campaign room" });
  return within(nav)
    .getAllByRole("link")
    .map((link) => [link.textContent, link.getAttribute("href")]);
}

test("links to the six tabs of the room", () => {
  render(<CampaignRoomTabs roomId="42" />);

  expect(tabLinks()).toEqual([
    ["Evidence", "/campaigns/42/evidence"],
    ["Brief", "/campaigns/42/brief"],
    ["Assets", "/campaigns/42/assets"],
    ["Launch", "/campaigns/42/launch"],
    ["Performance", "/campaigns/42/performance"],
    ["Results", "/campaigns/42/results"],
  ]);
});

test("encodes room ids that contain URL characters", () => {
  render(<CampaignRoomTabs roomId="a/b?c" />);

  expect(tabLinks()[0]).toEqual(["Evidence", "/campaigns/a%2Fb%3Fc/evidence"]);
});
