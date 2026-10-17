import { render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

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
    method: null,
    clientId: null,
    scopes: ["https://www.googleapis.com/auth/adwords"],
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
  render(<ConnectionList connections={states} canManage={false} brandId="brand-1" timeZone="Asia/Kolkata" />);

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
      brandId="brand-1" timeZone="Asia/Kolkata"
    />,
  );

  const googleAds = screen.getByRole("listitem", { name: "Google Ads" });
  expect(within(googleAds).getByRole("link", { name: "Connect with Google" }).getAttribute("href")).toBe(
    "/api/brands/brand-1/connections/x%2Fy/connect",
  );
  const analytics = screen.getByRole("listitem", { name: "Google Analytics" });
  expect(within(analytics).getByRole("link", { name: "Reconnect with Google" }).getAttribute("href")).toBe(
    "/api/brands/brand-1/connections/ga4/connect",
  );
});

test.each([
  ["cancelled", "Google consent was cancelled."],
  ["expired", "Google consent expired. Please try again."],
  ["scopes_missing", "The requested Google access was not granted."],
  ["anything-else", "Google connection failed. Please try again."],
])("explains callback error %s without echoing its raw value", (callbackError, message) => {
  render(<ConnectionList connections={[]} canManage brandId="brand-1" timeZone="Asia/Kolkata" callbackError={callbackError} />);
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
      brandId="brand-1" timeZone="Asia/Kolkata"
    />,
  );
  const row = screen.getByRole("listitem", { name: "Google Ads" });
  expect(row.className).toContain("min-w-0");
  expect(within(row).getByText(/very long reason/).className).toContain("wrap-anywhere");
});

test("shows an account picker from connection state even when callback choose points elsewhere", () => {
  render(
    <ConnectionList
      connections={[states[1], states[2]]}
      canManage
      brandId="brand-1" timeZone="Asia/Kolkata"
      callbackChoose="merchant-center"
      accountChoices={{ youtube: { accounts: [{ id: "yt-1", name: "Channel", managerId: null }] } }}
      chooseAccount={vi.fn(async () => ({ status: "saved" as const }))}
    />,
  );

  const youtube = screen.getByRole("listitem", { name: "YouTube" });
  expect(within(youtube).getByRole("combobox", { name: "Account" })).toBeTruthy();
  expect(within(youtube).getByRole("button", { name: "Use account" })).toBeTruthy();
  expect(within(screen.getByRole("listitem", { name: "Merchant Center" })).queryByRole("combobox")).toBeNull();
});

test("keeps account discovery failures local to the affected product", () => {
  render(
    <ConnectionList
      connections={[states[1], states[2]]}
      canManage
      brandId="brand-1" timeZone="Asia/Kolkata"
      accountChoices={{ youtube: { error: "Google did not answer." } }}
      chooseAccount={vi.fn(async () => ({ status: "saved" as const }))}
    />,
  );

  expect(within(screen.getByRole("listitem", { name: "YouTube" })).getByRole("alert").textContent).toContain("Google did not answer.");
  expect(screen.getByRole("listitem", { name: "Merchant Center" })).toBeTruthy();
});

test("highlights only the matching account choice returned from Google", () => {
  render(
    <ConnectionList
      connections={[states[1], states[2]]}
      canManage
      brandId="brand-1" timeZone="Asia/Kolkata"
      callbackChoose="youtube"
      accountChoices={{ youtube: { accounts: [] } }}
      chooseAccount={vi.fn(async () => ({ status: "saved" as const }))}
    />,
  );
  expect(screen.getByRole("listitem", { name: "YouTube" }).className).toContain("border-primary");
  expect(screen.getByRole("listitem", { name: "Merchant Center" }).className).not.toContain("border-primary");
});

test("lets an admin restart consent when no account is available", () => {
  render(
    <ConnectionList
      connections={[states[1]]}
      canManage
      brandId="brand-1" timeZone="Asia/Kolkata"
      accountChoices={{ youtube: { accounts: [] } }}
      chooseAccount={vi.fn(async () => ({ status: "saved" as const }))}
    />,
  );
  expect(screen.getByRole("link", { name: "Connect again with Google" }).getAttribute("href")).toBe(
    "/api/brands/brand-1/connections/youtube/connect",
  );
});

test("offers client credentials beside Google sign-in on every row that is not connected", () => {
  const action = vi.fn(async () => ({ status: "saved" as const }));
  render(<ConnectionList connections={states} canManage brandId="brand-1" timeZone="Asia/Kolkata" connectWithCredentials={action} />);

  for (const name of ["Google Ads", "YouTube", "Google Analytics"]) {
    expect(within(screen.getByRole("listitem", { name })).getByRole("button", { name: "Use client credentials" })).toBeTruthy();
  }
  expect(within(screen.getByRole("listitem", { name: "Merchant Center" })).queryByRole("button", { name: "Use client credentials" })).toBeNull();
});

test("members never receive the client credentials option", () => {
  const action = vi.fn(async () => ({ status: "saved" as const }));
  render(<ConnectionList connections={states} canManage={false} brandId="brand-1" timeZone="Asia/Kolkata" connectWithCredentials={action} />);
  expect(screen.queryByRole("button", { name: "Use client credentials" })).toBeNull();
});

test("shows the client ID of a connection made with client credentials", () => {
  render(<ConnectionList
    connections={[connection({ state: "connected", method: "clientCredentials", clientId: "brand.apps.googleusercontent.com", externalId: "ads-1" })]}
    canManage={false}
    brandId="brand-1"
    timeZone="Asia/Kolkata"
  />);

  const row = screen.getByRole("listitem", { name: "Google Ads" });
  expect(within(row).getByText(/Connected with client credentials/)).toBeTruthy();
  expect(within(row).getByText("brand.apps.googleusercontent.com")).toBeTruthy();
});

test("does not label Google sign-in connections as client credentials", () => {
  render(<ConnectionList
    connections={[connection({ state: "connected", method: "google", externalId: "ads-1" })]}
    canManage={false}
    brandId="brand-1"
    timeZone="Asia/Kolkata"
  />);

  expect(screen.queryByText(/client credentials/)).toBeNull();
});

test("offers admin checks only for connected rows and disconnect for active connections", () => {
  const action = vi.fn(async () => ({ status: "saved" as const }));
  render(<ConnectionList connections={states} canManage brandId="brand-1" timeZone="Asia/Kolkata" checkConnection={action} disconnectConnection={action} />);

  expect(within(screen.getByRole("listitem", { name: "Google Ads" })).queryByRole("button", { name: /check access|disconnect/i })).toBeNull();
  expect(within(screen.getByRole("listitem", { name: "YouTube" })).queryByRole("button", { name: "Check access" })).toBeNull();
  expect(within(screen.getByRole("listitem", { name: "YouTube" })).getByRole("button", { name: "Disconnect" })).toBeTruthy();
  expect(within(screen.getByRole("listitem", { name: "Merchant Center" })).getByRole("button", { name: "Check access" })).toBeTruthy();
  expect(within(screen.getByRole("listitem", { name: "Merchant Center" })).getByRole("button", { name: "Disconnect" })).toBeTruthy();
  expect(within(screen.getByRole("listitem", { name: "Google Analytics" })).queryByRole("button", { name: "Check access" })).toBeNull();
  expect(within(screen.getByRole("listitem", { name: "Google Analytics" })).getByRole("button", { name: "Disconnect" })).toBeTruthy();
});

test("members never receive check or disconnect controls", () => {
  const action = vi.fn(async () => ({ status: "saved" as const }));
  render(<ConnectionList connections={states} canManage={false} brandId="brand-1" timeZone="Asia/Kolkata" checkConnection={action} disconnectConnection={action} />);
  expect(screen.queryByRole("button", { name: /check access|disconnect/i })).toBeNull();
});

test("distinguishes connected accounts that share a display name", () => {
  render(<ConnectionList
    connections={[
      connection({ product: "google-ads", name: "Google Ads", state: "connected", displayName: "Main account", externalId: "ads-123" }),
      connection({ product: "youtube", name: "YouTube", state: "connected", displayName: "Main account", externalId: "channel-456" }),
    ]}
    canManage={false}
    brandId="brand-1"
    timeZone="Asia/Kolkata"
  />);

  expect(within(screen.getByRole("listitem", { name: "Google Ads" })).getByText("ads-123")).toBeTruthy();
  expect(within(screen.getByRole("listitem", { name: "YouTube" })).getByText("channel-456")).toBeTruthy();
});
