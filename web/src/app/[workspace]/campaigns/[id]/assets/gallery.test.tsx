import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import { AssetGallery } from "./_components/asset-gallery";
import { AssetMedia } from "./_components/asset-media";
import { asset, assetSet, brandId, campaignId, history } from "./fixtures.test-data";

const { load } = vi.hoisted(() => ({ load: vi.fn() }));
vi.mock("./history-action", () => ({ loadAssetHistory: load }));
const props = { brandId, campaignId, handle: "acme", timeZone: "Asia/Kolkata", language: "", campaignType: "" };
beforeEach(() => { vi.clearAllMocks(); load.mockResolvedValue({ history: history() }); });

test("groups actual assets with URL filters and honest check state", () => {
  render(<AssetGallery {...props} set={assetSet([asset(), asset({ id: "hindi", language: "hi" }), asset({ id: "shared", kind: "image", language: "zxx", campaignType: "demand_gen" })])} />);
  expect(screen.getByRole("heading", { name: "Search · Hindi" })).toBeTruthy();
  expect(screen.getByRole("heading", { name: "Demand Gen · Shared across languages" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Hindi" }).getAttribute("href")).toContain("language=hi");
  expect(screen.getAllByText("Not checked yet")).toHaveLength(3);
  expect(screen.queryByText(/score/i)).toBeNull(); expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
  expect(document.querySelector('[lang="hi"]')).toBeTruthy();
});
test("previews change headline combination and retain all saved copy", () => {
  render(<AssetGallery {...props} set={assetSet()} />);
  fireEvent.change(screen.getByLabelText("Preview headline"), { target: { value: "1" } });
  expect(screen.getByText("Find your next pair | Comfort for every step | Made for your everyday")).toBeTruthy();
  expect(screen.getByText("Illustrative Search preview. Placements and combinations may vary.")).toBeTruthy();
});
test("renders empty filters and read-only blockers without invented actions", () => {
  const { rerender } = render(<AssetGallery {...props} set={assetSet([])} />);
  expect(screen.getByRole("heading", { name: "No assets yet" })).toBeTruthy();
  rerender(<AssetGallery {...props} set={assetSet([asset()], { canEdit: false, editBlocker: "Wait for the Create agent to finish." })} />);
  expect(screen.getByText("Wait for the Create agent to finish.")).toBeTruthy(); expect(screen.queryByRole("button", { name: "Edit" })).toBeNull();
});
test("history loads on demand, explains provenance and retries failures", async () => {
  load.mockResolvedValueOnce({ error: "Unavailable" });
  render(<AssetGallery {...props} set={assetSet()} />);
  expect(load).not.toHaveBeenCalled(); fireEvent.click(screen.getByRole("button", { name: /View history/ }));
  expect(await screen.findByText("Unavailable")).toBeTruthy(); fireEvent.click(screen.getByRole("button", { name: "Try history again" }));
  expect(await screen.findByText(/Version 1 · generated/)).toBeTruthy();
  expect(screen.getByText("gemini · create-v1")).toBeTruthy(); expect(screen.getByRole("link", { name: /Source photo 1/ }).getAttribute("rel")).toContain("noopener");
  expect(load).toHaveBeenCalledWith(brandId, campaignId, asset().id);
});
test("shows labels, blockers, locked and AI draft states", () => {
  render(<AssetGallery {...props} set={assetSet([asset({ locked: true, disclosure: { ...asset().disclosure, blocker: "Political content needs review", syntheticMedia: true, platformLabels: [{ platform: "Google Ads", setting: "AI label", reason: "Required for this content", source: "javascript:test" }] } })])} />);
  expect(screen.getByText("Locked")).toBeTruthy(); expect(screen.getByText("Drafted by Kairo")).toBeTruthy(); expect(screen.getByText("Political content needs review")).toBeTruthy(); expect(screen.queryByRole("link", { name: /Disclosure requirements/ })).toBeNull();
});
test("native video has no autoplay and missing media offers an honest fallback", () => {
  const entry = asset({ kind: "video", width: 1080, height: 1920, mediaUrl: `/api/brands/${brandId}/campaigns/${campaignId}/assets/${asset().id}/file?version=1` });
  const { container } = render(<AssetMedia asset={entry} brandId={brandId} campaignId={campaignId} />);
  const video = container.querySelector("video")!; expect(video.controls).toBe(true); expect(video.autoplay).toBe(false); expect(video.preload).toBe("none");
  fireEvent.error(video); expect(screen.getByText(/Media unavailable/)).toBeTruthy();
});
test("shopping preview displays proposed title without invented product prices", () => {
  const entry = asset({ kind: "shopping_title", campaignType: "shopping", content: { ...asset().content, title: "Acme everyday shoes", currentTitle: "Old catalogue title", headlines: null } });
  render(<AssetGallery {...props} set={assetSet([entry])} />);
  const card = screen.getByRole("article", { name: /Shopping title/ }); expect(within(card).getByText("Acme everyday shoes")).toBeTruthy(); expect(within(card).getByText(/No product image supplied/)).toBeTruthy();
  expect(screen.getByRole("status").textContent).toBe("1 saved asset");
});
