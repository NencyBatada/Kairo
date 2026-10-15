import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { KitFiles } from "./kit-files";

const { refresh, replace } = vi.hoisted(() => ({ refresh: vi.fn(), replace: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh, replace }), usePathname: () => "/acme/brand-kit" }));
const remove = {};
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

test("uploads multipart data with the request header and refreshes available files", async () => {
  const fetch = vi.fn(async () => new Response("{}", { status: 201 }));
  vi.stubGlobal("fetch", fetch);
  render(<KitFiles brandId="brand-1" files={[]} canEdit remove={remove} />);
  const file = new File(["%PDF-1.7"], "guide.pdf", { type: "application/pdf" });
  fireEvent.change(screen.getByLabelText("Choose file"), { target: { files: [file] } });
  fireEvent.click(screen.getByRole("button", { name: "Upload file" }));
  await waitFor(() => expect(refresh).toHaveBeenCalledOnce());
  const [url, options] = fetch.mock.calls[0] as unknown as [string, RequestInit];
  expect(url).toBe("/api/brands/brand-1/brand-kit/files");
  expect(options.headers).toEqual({ "X-Kairo-Request": "1" });
  expect((options.body as FormData).get("file")).toBe(file);
  expect(screen.getByRole("status").textContent).toContain("Select it in the draft");
});

test("rejects a file above 20 MB before uploading", async () => {
  const fetch = vi.fn(); vi.stubGlobal("fetch", fetch);
  render(<KitFiles brandId="b" files={[]} canEdit remove={remove} />);
  const file = new File(["x"], "big.pdf"); Object.defineProperty(file, "size", { value: 20 * 1024 * 1024 + 1 });
  fireEvent.change(screen.getByLabelText("Choose file"), { target: { files: [file] } });
  fireEvent.click(screen.getByRole("button", { name: "Upload file" }));
  await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("20 MB"));
  expect(fetch).not.toHaveBeenCalled();
});

test("shows upload validation and network failures without refreshing", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ errors: { file: ["Upload a valid PDF."] } }), { status: 400 })).mockRejectedValueOnce(new Error("offline"));
  vi.stubGlobal("fetch", fetch);
  render(<KitFiles brandId="b" files={[]} canEdit remove={remove} />);
  const input = screen.getByLabelText("Choose file");
  fireEvent.change(input, { target: { files: [new File(["x"], "guide.pdf")] } });
  fireEvent.click(screen.getByRole("button", { name: "Upload file" }));
  await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("Upload a valid PDF."));
  fireEvent.click(screen.getByRole("button", { name: "Upload file" }));
  await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("try again"));
  expect(refresh).not.toHaveBeenCalled();
});

test("read-only members have no upload controls", () => {
  render(<KitFiles brandId="b" files={[]} canEdit={false} remove={remove} />);
  expect(screen.queryByLabelText("Choose file")).toBeNull();
});
