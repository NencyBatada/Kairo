import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import ConnectionsPage from "./page";

const { getWorkspace, getConnections, coreApi, unwrap } = vi.hoisted(() => ({
  getWorkspace: vi.fn(),
  getConnections: vi.fn(),
  coreApi: vi.fn(),
  unwrap: vi.fn(),
}));

vi.mock("@/lib/core-api/queries", () => ({ getWorkspace }));
vi.mock("@/lib/core-api/client", () => ({ coreApi, unwrap }));

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
