import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { AccessibleMedia } from "./accessible-media";
import { accessibilityUrl, alternativeFileUrl, type AlternativeView } from "@/lib/media-accessibility";

const base = "/api/brands/11111111-1111-1111-1111-111111111111/media-accessibility?kind=asset&id=22222222-2222-2222-2222-222222222222&version=1&campaignId=33333333-3333-3333-3333-333333333333";
function view(changes: Partial<AlternativeView> = {}): AlternativeView {
  const revision = changes.revision ?? 1, hash = changes.sourceHash ?? "A".repeat(64);
  return { target: { kind: "asset", id: "22222222-2222-2222-2222-222222222222", version: 1, campaignId: "33333333-3333-3333-3333-333333333333" }, revision: 1, sourceHash: "A".repeat(64), content: { language: "hi", altText: "लाल जूता", transcript: "नमस्ते", captions: [{ start: "0", end: "2", text: "नमस्ते" }], visualDescription: "A red shoe.", audioConveysVisuals: true, silentVideo: false }, canEdit: true, status: "draft", missing: [], reviewedBy: null, reviewedAt: null, captionsUrl: `${base}&file=captions&revision=${revision}&sourceHash=${hash}`, describedMediaUrl: null, canDraft: false, ...changes };
}
function respond(value = view()) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(value), { status: 200 }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}
afterEach(() => vi.unstubAllGlobals());

test("serves real caption tracks and language-labelled transcripts without claiming a draft is reviewed", async () => {
  respond();
  const { container } = render(<AccessibleMedia src="/media.mp4" metadataUrl={base} contentType="video/mp4" label="Campaign video" />);
  await screen.findByText(/Draft alternatives/);
  expect(container.querySelector("track")?.getAttribute("src")).toBe(`${base}&file=captions&revision=1&sourceHash=${"A".repeat(64)}`);
  expect(container.querySelector("track")?.getAttribute("srclang")).toBe("hi");
  expect(screen.getAllByText("नमस्ते")[0].closest("[lang]")?.getAttribute("lang")).toBe("hi");
  expect(screen.queryByText("Alternatives reviewed against this media version.")).toBeNull();
});

test("absent metadata does not render an empty caption track", () => {
  const { container } = render(<AccessibleMedia src="/media.mp4" contentType="video/mp4" label="Campaign video" />);
  expect(container.querySelector("track")).toBeNull();
  expect(screen.getByText("Media alternatives are incomplete.")).toBeTruthy();
});

test("substituted caption links cannot retain a reviewed-complete label", async () => {
  respond(view({ status: "reviewed", captionsUrl: "https://evil.example/captions.vtt" }));
  const { container } = render(<AccessibleMedia src="/media.mp4" metadataUrl={base} contentType="video/mp4" label="Campaign video" />);
  await screen.findByRole("alert");
  expect(container.querySelector("track")).toBeNull();
  expect(screen.queryByText("Alternatives reviewed against this media version.")).toBeNull();
});

test("review requires an explicit acknowledgement of the saved version", async () => {
  const fetch = respond();
  render(<AccessibleMedia src="/media.mp4" metadataUrl={base} contentType="video/mp4" label="Campaign video" />);
  const confirm = await screen.findByRole("button", { name: "Confirm alternatives reviewed", hidden: true });
  expect((confirm as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByText("Edit media alternatives"));
  fireEvent.click(screen.getByLabelText(/I checked these saved alternatives/));
  expect((confirm as HTMLButtonElement).disabled).toBe(false);
  fetch.mockResolvedValueOnce(new Response(JSON.stringify(view({ status: "reviewed", revision: 2, reviewedBy: "reviewer", reviewedAt: "2026-10-23T00:00:00Z" })), { status: 200 }));
  fireEvent.click(confirm);
  await screen.findByText("Alternatives reviewed and saved.");
  expect(fetch.mock.calls[1][0]).toBe(base.split("?")[0] + "/confirm");
  expect(JSON.parse(fetch.mock.calls[1][1].body).sourceHash).toBe("A".repeat(64));
  expect(screen.getByText("Alternatives reviewed against this media version.")).toBeTruthy();
});

test("failed saves preserve the editor's work and announce the error", async () => {
  const fetch = respond();
  render(<AccessibleMedia src="/image.png" metadataUrl={base} contentType="image/png" label="Product image" />);
  await screen.findByText("Edit media alternatives");
  fireEvent.click(screen.getByText("Edit media alternatives"));
  const input = screen.getByLabelText("Image description");
  fireEvent.change(input, { target: { value: "My corrected description" } });
  fetch.mockResolvedValueOnce(new Response(JSON.stringify({ title: "The alternatives changed. Reload before saving." }), { status: 409 }));
  fireEvent.click(screen.getByRole("button", { name: "Save draft alternatives" }));
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "The alternatives changed. Reload before saving.");
  expect((input as HTMLTextAreaElement).value).toBe("My corrected description");
});

test("read-only users see alternatives without editor or generation controls", async () => {
  respond(view({ canEdit: false }));
  render(<AccessibleMedia src="/media.mp4" metadataUrl={base} contentType="video/mp4" label="Campaign video" />);
  await screen.findByText(/Draft alternatives/);
  expect(screen.queryByText("Edit media alternatives")).toBeNull();
  expect(screen.queryByRole("button", { name: /Draft alternatives from/ })).toBeNull();
});

test("failed caption delivery cannot display a reviewed-complete message", async () => {
  const fetch = respond(view({ status: "reviewed" }));
  const { container } = render(<AccessibleMedia src="/media.mp4" metadataUrl={base} contentType="video/mp4" label="Campaign video" />);
  await screen.findByText("Alternatives reviewed against this media version.");
  fireEvent.error(container.querySelector("track")!);
  await screen.findByRole("alert");
  await waitFor(() => expect(screen.queryByText("Alternatives reviewed against this media version.")).toBeNull());
  fireEvent.click(screen.getByText("Edit media alternatives"));
  expect((screen.getByLabelText(/I checked these saved alternatives/) as HTMLInputElement).disabled).toBe(true);
  fireEvent.change(screen.getByLabelText("Essential visual information"), { target: { value: "Corrected visual description" } });
  fetch.mockResolvedValueOnce(new Response(JSON.stringify(view({ revision: 2 })), { status: 200 }));
  fireEvent.click(screen.getByRole("button", { name: "Save draft alternatives" }));
  await screen.findByText("Draft saved. Check it against the media before confirming.");
  expect((screen.getByLabelText(/I checked these saved alternatives/) as HTMLInputElement).disabled).toBe(true);
  expect((screen.getByRole("button", { name: "Confirm alternatives reviewed" }) as HTMLButtonElement).disabled).toBe(true);
});

test("alternative links cannot fetch an external or substituted media URL", () => {
  expect(accessibilityUrl("https://evil.example/" + base)).toBeNull();
  expect(accessibilityUrl(base + "&file=described")).toBeNull();
  expect(alternativeFileUrl(base, "https://evil.example/captions.vtt", "captions", 1, "A".repeat(64))).toBeUndefined();
  expect(alternativeFileUrl(base, base + "&file=captions&revision=1&sourceHash=ABC", "captions", 1, "ABC")).toBe(base + "&file=captions&revision=1&sourceHash=ABC");
  expect(alternativeFileUrl(base, base + "&file=captions&revision=2&sourceHash=ABC", "captions", 1, "ABC")).toBeUndefined();
});
