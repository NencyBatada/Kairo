import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Link from "next/link";
import { beforeEach, expect, test, vi } from "vitest";
import { StudioWorkspace } from "./_components/studio-workspace";
import { asset, assetSet, brandId, campaignId } from "./fixtures.test-data";
import type { Asset, AssetSet } from "./_lib/model";

const { save, lock, regenerate, refresh } = vi.hoisted(() => ({ save: vi.fn(), lock: vi.fn(), regenerate: vi.fn(), refresh: vi.fn() }));
vi.mock("./actions", () => ({ saveAsset: save, setAssetLock: lock, regenerateAsset: regenerate }));
vi.mock("./history-action", () => ({ loadAssetHistory: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));
const props = { brandId, campaignId, handle: "acme", timeZone: "Asia/Kolkata", language: "", campaignType: "", runs: [] };
const title = () => asset({ kind: "shopping_title", campaignType: "shopping", content: { ...asset().content, headlines: null, title: "New campaign title", currentTitle: "Current feed title" } });
function studio(entries: Asset[] = [title()], extra: Partial<AssetSet> = {}) { return <StudioWorkspace {...props} set={assetSet(entries, extra)} />; }
beforeEach(() => { vi.clearAllMocks(); save.mockResolvedValue({ status: "error", message: "Could not save", fieldErrors: { title: ["Check title"] } }); lock.mockResolvedValue({ status: "saved", message: "Locked", asset: { ...title(), locked: true } }); regenerate.mockResolvedValue({ status: "error", message: "Try generation again" }); });

test("compares shopping titles for viewers and never shows mutation actions", () => {
  render(studio([title()], { canEdit: false })); expect(screen.getByText("Current feed title")).toBeTruthy(); expect(screen.getAllByText("New campaign title")).toHaveLength(2); expect(screen.queryByRole("button", { name: "Edit asset" })).toBeNull(); expect(screen.queryByRole("button", { name: "Lock asset" })).toBeNull();
});
test("failed saves retain input, field errors and original version", async () => {
  render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.change(screen.getByLabelText("Campaign shopping title"), { target: { value: "My own edited title" } }); fireEvent.click(screen.getByRole("button", { name: "Save asset" }));
  expect(await screen.findByText("Could not save")).toBeTruthy(); expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).value).toBe("My own edited title"); expect(screen.getByText("Check title")).toBeTruthy(); expect(save.mock.calls[0][4].get("version")).toBe("1"); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert").parentElement));
});
test("success replaces the saved title and clears the editor", async () => {
  save.mockResolvedValue({ status: "saved", message: "Asset saved", asset: { ...title(), version: 2, editedByPerson: true, content: { ...title().content, title: "Saved campaign title" } } });
  render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.change(screen.getByLabelText("Campaign shopping title"), { target: { value: "Saved campaign title" } }); fireEvent.click(screen.getByRole("button", { name: "Save asset" })); expect(await screen.findByText("Asset saved")).toBeTruthy(); expect(screen.queryByLabelText("Campaign shopping title")).toBeNull(); expect(screen.getAllByText("Saved campaign title")).toHaveLength(2); expect(screen.queryByText("Drafted by Kairo")).toBeNull();
});
test("discard requires confirmation and cancelling retains the edit", async () => {
  render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.change(screen.getByLabelText("Campaign shopping title"), { target: { value: "Unsaved title" } }); fireEvent.click(screen.getByRole("button", { name: "Discard edits" }));
  const dialog = await screen.findByRole("alertdialog"); await waitFor(() => expect(document.activeElement).toBe(within(dialog).getByRole("button", { name: "Cancel" }))); fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" })); expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).value).toBe("Unsaved title");
  fireEvent.click(screen.getByRole("button", { name: "Discard edits" })); fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Discard changes" })); await waitFor(() => expect(screen.queryByLabelText("Campaign shopping title")).toBeNull()); expect(save).not.toHaveBeenCalled();
});
test("live updates preserve edits and block saving a stale baseline", async () => {
  const { rerender } = render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.change(screen.getByLabelText("Campaign shopping title"), { target: { value: "Unsaved title" } }); rerender(studio([{ ...title(), version: 2, content: { ...title().content, title: "Remote title" } }]));
  expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).value).toBe("Unsaved title"); expect((screen.getByRole("button", { name: "Save asset" }) as HTMLButtonElement).disabled).toBe(true); fireEvent.click(screen.getByRole("button", { name: "Reload latest" })); fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Discard changes" })); await waitFor(() => expect(refresh).toHaveBeenCalled());
});
test("open edits block other controls and filter navigation", () => {
  render(studio([title(), asset({ id: "second" })])); const cards = screen.getAllByRole("article"); fireEvent.click(within(cards[0]).getByRole("button", { name: "Edit asset" })); expect((within(cards[1]).getByRole("button", { name: "Edit asset" }) as HTMLButtonElement).disabled).toBe(true); fireEvent.click(screen.getByRole("link", { name: "All languages" })); expect(screen.getByText(/Save or discard it before following/)).toBeTruthy();
});
test("locks and unlocks using the saved version, keeping locked text read-only", async () => {
  render(studio()); fireEvent.click(screen.getByRole("button", { name: "Lock asset" })); expect(await screen.findByRole("button", { name: "Unlock asset" })).toBeTruthy(); expect(lock).toHaveBeenCalledWith(brandId, campaignId, title().id, 1, true); expect((screen.getByRole("button", { name: "Edit asset" }) as HTMLButtonElement).disabled).toBe(true);
  await waitFor(() => expect((screen.getByRole("button", { name: "Unlock asset" }) as HTMLButtonElement).disabled).toBe(false)); lock.mockResolvedValue({ status: "saved", message: "Unlocked", asset: title() }); fireEvent.click(screen.getByRole("button", { name: "Unlock asset" })); expect(await screen.findByText("Unlocked")).toBeTruthy(); await waitFor(() => expect((screen.getByRole("button", { name: "Edit asset" }) as HTMLButtonElement).disabled).toBe(false));
});
test("regeneration requires explicit confirmation and preserves failed instructions", async () => {
  render(studio()); fireEvent.click(screen.getByRole("button", { name: "Regenerate" })); fireEvent.change(screen.getByLabelText("Regeneration instruction"), { target: { value: "Lead with comfort" } }); fireEvent.click(screen.getByRole("button", { name: "Review regeneration" })); const dialog = await screen.findByRole("alertdialog"); expect(regenerate).not.toHaveBeenCalled(); fireEvent.click(within(dialog).getByRole("button", { name: "Regenerate asset" })); expect(await within(dialog).findByText("Try generation again")).toBeTruthy(); expect((screen.getByLabelText("Regeneration instruction") as HTMLTextAreaElement).value).toBe("Lead with comfort");
  await waitFor(() => expect((within(dialog).getByRole("button", { name: "Regenerate asset" }) as HTMLButtonElement).disabled).toBe(false)); regenerate.mockResolvedValue({ status: "saved", message: "Queued", runId: "run-1" }); fireEvent.click(within(dialog).getByRole("button", { name: "Regenerate asset" })); expect(await screen.findByText("Queued")).toBeTruthy(); expect(screen.getByText(/Saved assets remain visible/)).toBeTruthy(); expect(regenerate).toHaveBeenLastCalledWith(brandId, campaignId, title().id, 1, "Lead with comfort");
});
test("unsupported supporting media can be locked but has no text or regeneration controls", () => {
  render(studio([asset({ kind: "voiceover" })])); expect(screen.queryByRole("button", { name: "Edit asset" })).toBeNull(); expect(screen.queryByRole("button", { name: "Regenerate" })).toBeNull(); expect(screen.getByRole("button", { name: "Lock asset" })).toBeTruthy();
});
test("permission and stage changes preserve an open draft and disable save", () => {
  const { rerender } = render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); rerender(studio([title()], { canEdit: false, editBlocker: "Wait for the Create agent to finish." })); expect(screen.getByLabelText("Campaign shopping title")).toBeTruthy(); expect((screen.getByRole("button", { name: "Save asset" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByText(/Editing is paused/)).toBeTruthy();
});
test("pending saves retain input and prevent duplicate submissions", async () => {
  let finish: (value: { status: "error"; message: string }) => void = () => {};
  save.mockImplementation(() => new Promise((resolve) => { finish = resolve; })); render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.click(screen.getByRole("button", { name: "Save asset" })); expect((screen.getByRole("button", { name: "Save asset" }) as HTMLButtonElement).disabled).toBe(true); await act(async () => finish({ status: "error", message: "Failed" })); expect(await screen.findByText("Failed")).toBeTruthy();
});
test("a remotely removed asset keeps its open draft available for copying", () => {
  const { rerender } = render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.change(screen.getByLabelText("Campaign shopping title"), { target: { value: "Keep this unsaved draft" } }); rerender(studio([])); expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).value).toBe("Keep this unsaved draft"); expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).disabled).toBe(false); expect((screen.getByRole("button", { name: "Save asset" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByText(/no longer in the current set/)).toBeTruthy();
});
test("opening untouched fields uses the latest server version", async () => {
  const { rerender } = render(studio()); rerender(studio([{ ...title(), version: 4, content: { ...title().content, title: "Latest server title" } }])); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).value).toBe("Latest server title"); fireEvent.click(screen.getByRole("button", { name: "Save asset" })); await screen.findByText("Could not save"); expect(save.mock.calls[0][4].get("version")).toBe("4");
});
test("keyword rows retain individual match types and support adding and removing drafts", async () => {
  const entry = asset({ kind: "keyword_set", content: { ...asset().content, headlines: null, keywords: [{ text: "everyday shoes", match: "phrase" }] } }); render(studio([entry])); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.click(screen.getByRole("button", { name: "Add keyword" })); fireEvent.change(screen.getByLabelText("Keyword 2"), { target: { value: "walking shoes" } }); fireEvent.change(screen.getByLabelText("Match type for keyword 2"), { target: { value: "exact" } }); fireEvent.click(screen.getByRole("button", { name: "Remove keyword 1" })); fireEvent.click(screen.getByRole("button", { name: "Save asset" })); await screen.findByText("Could not save"); expect(save.mock.calls[0][4].getAll("keywordText")).toEqual(["walking shoes"]); expect(save.mock.calls[0][4].getAll("keywordMatch")).toEqual(["exact"]);
});
test("API conflicts preserve local fields until an explicit reload", async () => {
  save.mockResolvedValue({ status: "error", message: "This asset changed", conflict: true }); render(studio()); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); fireEvent.change(screen.getByLabelText("Campaign shopping title"), { target: { value: "My draft" } }); fireEvent.click(screen.getByRole("button", { name: "Save asset" })); await screen.findByText("This asset changed"); expect((screen.getByLabelText("Campaign shopping title") as HTMLTextAreaElement).value).toBe("My draft"); expect((screen.getByRole("button", { name: "Save asset" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByRole("button", { name: "Reload latest" })).toBeTruthy();
});
test("unsaved work protects campaign tabs outside the gallery and browser reloads", () => {
  render(<><Link href="/acme/campaigns/another">Another campaign tab</Link>{studio()}</>); fireEvent.click(screen.getByRole("button", { name: "Edit asset" })); const click = new MouseEvent("click", { bubbles: true, cancelable: true }); fireEvent(screen.getByRole("link", { name: "Another campaign tab" }), click); expect(click.defaultPrevented).toBe(true); const unload = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(unload); expect(unload.defaultPrevented).toBe(true);
});
