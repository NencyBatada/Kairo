import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import type { ConnectionView } from "@/lib/core-api/types";

import { ConnectionList } from "./connection-list";

function connection(overrides: Partial<ConnectionView>): ConnectionView {
  return {
    product: "google-ads",
    name: "Google Ads",
    state: "notConnected",
    externalId: null,
    displayName: null,
    connectedBy: null,
    connectedByName: null,
    connectedAt: null,
    checkedAt: null,
    reconnectReason: null,
    ...overrides,
  };
}

const states = [
  connection({ product: "google-ads", name: "Google Ads" }),
  connection({ product: "youtube", name: "YouTube", state: "choosingAccount", connectedByName: "Asha" }),
  connection({
    product: "merchant-center",
    name: "Merchant Center",
    state: "connected",
    externalId: "123456",
    displayName: "Footwear shop",
    connectedByName: "Nency",
    checkedAt: "2026-10-14T05:00:00Z",
  }),
  connection({ product: "ga4", name: "Google Analytics", state: "needsReconnect", reconnectReason: "Access expired" }),
];

test("shows all connection states and account details to a member without management controls", () => {
  render(<ConnectionList connections={states} canManage={false} brandId="brand-1" />);

  expect(screen.getByText("Not connected")).toBeTruthy();
  expect(screen.getByText("Choose account")).toBeTruthy();
  expect(screen.getByText("Connected")).toBeTruthy();
  expect(screen.getByText("Needs reconnection")).toBeTruthy();
  expect(screen.getByText("Footwear shop")).toBeTruthy();
  expect(screen.getByText("Nency")).toBeTruthy();
  expect(screen.getByText("Access expired")).toBeTruthy();
  expect(screen.queryByRole("link", { name: /connect/i })).toBeNull();
  expect(screen.queryByRole("button", { name: /disconnect|check access/i })).toBeNull();
});

test("offers admin consent links through the same-origin API proxy with encoded product slugs", () => {
  render(
    <ConnectionList
      connections={[connection({ product: "x/y", name: "Google Ads" }), states[3]]}
      canManage
      brandId="brand-1"
    />,
  );

  const googleAds = screen.getByRole("listitem", { name: "Google Ads" });
  expect(within(googleAds).getByRole("link", { name: "Connect" }).getAttribute("href")).toBe(
    "/api/brands/brand-1/connections/x%2Fy/connect",
  );
  const analytics = screen.getByRole("listitem", { name: "Google Analytics" });
  expect(within(analytics).getByRole("link", { name: "Reconnect" }).getAttribute("href")).toBe(
    "/api/brands/brand-1/connections/ga4/connect",
  );
});

test.each([
  ["cancelled", "Google consent was cancelled."],
  ["expired", "Google consent expired. Please try again."],
  ["scopes_missing", "The requested Google access was not granted."],
  ["anything-else", "Google connection failed. Please try again."],
])("explains callback error %s without echoing its raw value", (callbackError, message) => {
  render(<ConnectionList connections={[]} canManage brandId="brand-1" callbackError={callbackError} />);
  expect(screen.getByRole("alert").textContent).toBe(message);
});

test("keeps long connection details inside the responsive row", () => {
  render(
    <ConnectionList
      connections={[
        connection({
          state: "needsReconnect",
          displayName: "A very long account name that should stay inside the connection row",
          reconnectReason: "A very long reason that should wrap inside the connection row",
        }),
      ]}
      canManage={false}
      brandId="brand-1"
    />,
  );
  const row = screen.getByRole("listitem", { name: "Google Ads" });
  expect(row.className).toContain("min-w-0");
  expect(within(row).getByText(/very long reason/).className).toContain("wrap-anywhere");
});
