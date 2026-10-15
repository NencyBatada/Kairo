import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { imported } from "../_lib/fixtures.test-data";
import { CatalogueSource } from "./catalogue-source";

const { refresh, replace } = vi.hoisted(() => ({ refresh: vi.fn(), replace: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh, replace }), usePathname: () => "/acme/brand-kit/catalogue" }));
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });
const props = { brandId: "b", handle: "acme", canManage: true, source: null, setFeed: vi.fn(), refreshFeed: vi.fn(), stopFeed: vi.fn() };

test("file upload sends explicit safeguard choice and reports held imports honestly", async () => {
  const fetch = vi.fn(async () => new Response(JSON.stringify({ ...imported, status: "suspicious" }), { status: 201 })); vi.stubGlobal("fetch", fetch);
  render(<CatalogueSource {...props} />);
  const file = new File(["id,title\n1,Shoe"], "products.csv");
  fireEvent.change(screen.getByLabelText("Catalogue file"), { target: { files: [file] } });
  fireEvent.click(screen.getByRole("button", { name: "Import file" }));
  await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("unchanged"));
  const options = (fetch.mock.calls[0] as unknown as [string, RequestInit])[1];
  expect(new Headers(options.headers).get("X-Kairo-Request")).toBe("1");
  expect((options.body as FormData).get("allowLargeRemoval")).toBe("false");
  expect(refresh).toHaveBeenCalledOnce();
});
test("oversized files never reach the service", async () => {
  const fetch = vi.fn(); vi.stubGlobal("fetch", fetch);
  render(<CatalogueSource {...props} />);
  const file = new File(["x"], "products.csv"); Object.defineProperty(file, "size", { value: 50 * 1024 * 1024 + 1 });
  fireEvent.change(screen.getByLabelText("Catalogue file"), { target: { files: [file] } });
  fireEvent.click(screen.getByRole("button", { name: "Import file" }));
  await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("50 MB"));
  expect(fetch).not.toHaveBeenCalled();
});
test("non-admins see source information without mutation controls", () => {
  render(<CatalogueSource {...props} canManage={false} source={{ kind: "feed", feedUrl: "https://example.com/feed", failuresInRow: 3 }} />);
  expect(screen.getByText(/https:\/\/example.com\/feed/)).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Import file" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Save feed" })).toBeNull();
});
