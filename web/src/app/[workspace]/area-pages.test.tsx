import { render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import ImpactLabPage from "./impact-lab/page";

vi.mock("@/lib/core-api/queries", () => ({ getWorkspace: async () => ({ id: "11111111-1111-1111-1111-111111111111", roles: ["marketer"] }) }));
vi.mock("@/lib/core-api/client", () => ({ coreApi: async () => ({ GET: async () => ({ data: { analyses: [] } }) }), unwrap: async (result: { data: unknown }) => result.data }));

test("Impact Lab has one page title, an honest empty state and a working setup link", async () => {
  render(await ImpactLabPage({ params: Promise.resolve({ workspace: "acme" }) }));

  expect(screen.getAllByRole("heading", { level: 1 }).map((heading) => heading.textContent)).toEqual(["Impact Lab"]);
  expect(screen.getByRole("heading", { name: "No analyses yet" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "New analysis" }).getAttribute("href")).toBe("/acme/impact-lab/new");
});
