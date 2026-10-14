import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import ConnectionsPage from "./page";

const { getWorkspace, getConnections, getAccounts, coreApi, signInAgain, unwrap } = vi.hoisted(() => ({
  getWorkspace: vi.fn(),
  getConnections: vi.fn(),
  getAccounts: vi.fn(),
  coreApi: vi.fn(),
  signInAgain: vi.fn(),
  unwrap: vi.fn(),
}));

vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, signInAgain, unwrap }));

test("loads the current workspace connections and limits management to admins", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  getConnections.mockResolvedValue({ data: [] });
  coreApi.mockResolvedValue({ GET: getConnections });
  unwrap.mockImplementation(async (result) => result.data);

  render(
    await ConnectionsPage({
      params: Promise.resolve({ workspace: "acme" }),
      searchParams: Promise.resolve({ error: "cancelled" }),
    }),
  );

  expect(getWorkspace).toHaveBeenCalledWith("acme");
  expect(getConnections).toHaveBeenCalledWith("/api/brands/{brandId}/connections", {
    params: { path: { brandId: "brand-1" } },
  });
  expect(screen.getByText("Only workspace admins can manage connections.")).toBeTruthy();
  expect(screen.getByRole("alert").textContent).toBe("Google consent was cancelled.");
});

test("discovers accounts for an admin only when a product is choosing an account", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["admin"] });
  getConnections.mockResolvedValue({ data: [{
    product: "youtube", name: "YouTube", state: "choosingAccount", externalId: null, displayName: null,
    connectedBy: null, connectedByName: null, connectedAt: null, checkedAt: null, reconnectReason: null,
  }] });
  getAccounts.mockResolvedValue({ data: [{ id: "yt-1", name: "Channel", managerId: null }], response: { status: 200, ok: true } });
  const get = vi.fn((path) => path.endsWith("/accounts") ? getAccounts() : getConnections());
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => result.data);

  render(await ConnectionsPage({
    params: Promise.resolve({ workspace: "acme" }),
    searchParams: Promise.resolve({ choose: "merchant-center" }),
  }));

  expect(get).toHaveBeenCalledWith("/api/brands/{brandId}/connections/{product}/accounts", {
    params: { path: { brandId: "brand-1", product: "youtube" } },
  });
  expect(screen.getByRole("combobox", { name: "Account" })).toBeTruthy();
});

test("keeps a failed account lookup local to its product", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["admin"] });
  getConnections.mockResolvedValue({ data: [{
    product: "youtube", name: "YouTube", state: "choosingAccount", externalId: null, displayName: null,
    connectedBy: null, connectedByName: null, connectedAt: null, checkedAt: null, reconnectReason: null,
  }] });
  getAccounts.mockResolvedValue({ error: { title: "Google didn't answer." }, response: { status: 409, ok: false } });
  coreApi.mockResolvedValue({ GET: vi.fn((path) => path.endsWith("/accounts") ? getAccounts() : getConnections()) });
  unwrap.mockImplementation(async (result) => result.data);

  render(await ConnectionsPage({ params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve({}) }));

  expect(screen.getByRole("listitem", { name: "YouTube" })).toBeTruthy();
  expect(screen.getByRole("alert").textContent).toContain("Google didn't answer.");
});

test("redirects to sign-in when account discovery loses the session", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["admin"] });
  getConnections.mockResolvedValue({ data: [{
    product: "youtube", name: "YouTube", state: "choosingAccount", externalId: null, displayName: null,
    connectedBy: null, connectedByName: null, connectedAt: null, checkedAt: null, reconnectReason: null,
  }] });
  getAccounts.mockResolvedValue({ response: { status: 401, ok: false } });
  coreApi.mockResolvedValue({ GET: vi.fn((path: string) => path.endsWith("/accounts") ? getAccounts() : getConnections()) });
  unwrap.mockImplementation(async (result) => result.data);
  signInAgain.mockImplementation(() => { throw new Error("sign-in redirect"); });

  await expect(ConnectionsPage({ params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve({}) }))
    .rejects.toThrow("sign-in redirect");
});

test("formats the last access check in the workspace time zone", async () => {
  getWorkspace.mockResolvedValue({ id: "brand-1", roles: ["marketer"] });
  getConnections.mockResolvedValue({ data: [{
    product: "youtube", name: "YouTube", state: "connected", externalId: "channel-1", displayName: "Channel",
    connectedBy: null, connectedByName: null, connectedAt: null, checkedAt: "2026-10-14T05:00:00Z", reconnectReason: null,
  }] });
  const getDetails = vi.fn().mockResolvedValue({ data: { timeZone: "Asia/Kolkata" } });
  const get = vi.fn((path: string) => path === "/api/brands/{brandId}" ? getDetails() : getConnections());
  coreApi.mockResolvedValue({ GET: get });
  unwrap.mockImplementation(async (result) => result.data);

  render(await ConnectionsPage({ params: Promise.resolve({ workspace: "acme" }), searchParams: Promise.resolve({}) }));

  expect(getDetails).toHaveBeenCalledOnce();
  expect(screen.getByText(/10:30/)).toBeTruthy();
});
