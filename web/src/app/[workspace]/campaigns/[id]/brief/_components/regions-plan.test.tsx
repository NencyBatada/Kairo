import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import { detail, draftRoom, proof } from "../fixtures.test-data";
import { BriefWorkspace } from "./brief-workspace";
import { RegionsPlan } from "./regions-plan";
import { RegionsMap } from "./regions-map";

const { chooseHoldouts, changeLockedPlan } = vi.hoisted(() => ({ chooseHoldouts: vi.fn(), changeLockedPlan: vi.fn() }));
vi.mock("../proof-actions", () => ({ chooseHoldouts, changeLockedPlan }));
vi.mock("../actions", () => ({ saveBrief: vi.fn(), proposeBrief: vi.fn(), applyBrief: vi.fn(), requestBriefApproval: vi.fn(), decideBrief: vi.fn() }));
const props = { result: proof, brandId: "b", campaignId: "c", campaignVersion: "1", canEdit: true };
beforeEach(() => { vi.clearAllMocks(); chooseHoldouts.mockResolvedValue({ status: "saved", message: "Recalculation queued" }); changeLockedPlan.mockResolvedValue({ status: "saved", message: "Recorded change" }); });
const choose = () => { fireEvent.click(screen.getByRole("checkbox", { name: /1. Pune/ })); fireEvent.click(screen.getByRole("checkbox", { name: /2. Nashik/ })); };

test("map has shape legend, real region centres, zoom controls and keyboard alternatives", () => {
  render(<RegionsPlan {...props} />); const map = screen.getByRole("img", { name: /Saved test and holdout regions/ }); const initial = map.getAttribute("viewBox"); fireEvent.click(screen.getByRole("button", { name: "Zoom in on regions map" })); expect(map.getAttribute("viewBox")).not.toBe(initial);
  fireEvent.click(screen.getByRole("button", { name: /1. Mumbai/ })); expect(screen.getByRole("button", { name: /1. Mumbai/ }).getAttribute("aria-pressed")).toBe("true"); fireEvent.click(screen.getByRole("button", { name: "Show all regions" })); expect(map.getAttribute("viewBox")).toBe(initial);
  expect(screen.getByText("14 days")).toBeTruthy(); expect(screen.getByText("13.0%")).toBeTruthy();
});
test("missing coordinates remain in the region list and are never fabricated", () => {
  render(<RegionsMap regions={[{ ...proof.plan!.holdouts[0], latitude: null, kind: "holdout", number: 1 }]} />);
  expect(screen.queryByRole("img")).toBeNull(); expect(screen.getByText(/No map coordinates/)).toBeTruthy(); expect(screen.getByRole("button", { name: /Pune/ }).matches(":disabled")).toBe(true);
});
test("holdout selections do not change the map or save until submitted", async () => {
  render(<RegionsPlan {...props} />); choose(); expect(chooseHoldouts).not.toHaveBeenCalled(); expect(screen.getByRole("img").querySelector("title")?.textContent).toBe("Saved test and holdout regions");
  fireEvent.click(screen.getByRole("button", { name: "Recalculate with these holdouts" })); await screen.findByText("Recalculation queued"); expect(chooseHoldouts).toHaveBeenCalledOnce(); const data = chooseHoldouts.mock.calls[0][2] as FormData; expect(data.getAll("regionIds")).toEqual(["nashik"]); expect(data.get("version")).toBe("1");
});
test("failed saves retain selections and show region errors", async () => {
  chooseHoldouts.mockResolvedValue({ status: "error", fieldErrors: { regionIds: ["Leave a test region"] } }); render(<RegionsPlan {...props} />); choose(); fireEvent.click(screen.getByRole("button", { name: "Recalculate with these holdouts" })); await screen.findByText("Leave a test region", { selector: "a" }); expect((screen.getByRole("checkbox", { name: /Nashik/ }) as HTMLInputElement).checked).toBe(true);
});
test("external changes preserve choices but prevent stale submission", () => {
  const view = render(<RegionsPlan {...props} />); choose(); view.rerender(<RegionsPlan {...props} campaignVersion="2" />); expect(screen.getByRole("button", { name: "Recalculate with these holdouts" }).matches(":disabled")).toBe(true); expect((screen.getByRole("checkbox", { name: /Nashik/ }) as HTMLInputElement).checked).toBe(true); fireEvent.click(screen.getByRole("button", { name: "Discard choices and load latest" })); expect((screen.getByRole("checkbox", { name: /Pune/ }) as HTMLInputElement).checked).toBe(true);
});
test("untouched holdout controls follow the latest campaign and plan automatically", () => {
  const view = render(<RegionsPlan {...props} />);
  view.rerender(<RegionsPlan {...props} campaignVersion="2" result={{ ...proof, plan: { ...proof.plan!, version: 2, holdouts: [proof.plan!.candidates[1]] } }} />);
  expect((screen.getByRole("checkbox", { name: /Nashik/ }) as HTMLInputElement).checked).toBe(true);
  expect(screen.getByRole("checkbox", { name: /Pune/ }).matches(":disabled")).toBe(false);
  expect(screen.queryByText(/campaign or test plan changed/)).toBeNull();
});
test("locked changes require a confirmation and reason, preserving errors inside it", async () => {
  changeLockedPlan.mockResolvedValue({ status: "error", fieldErrors: { reason: ["Explain the closure"] } });
  const locked = { ...proof, canChooseHoldouts: false, canChangeAfterLock: true, plan: { ...proof.plan!, lockedAt: "2026-10-16T00:00:00Z" } };
  render(<RegionsPlan {...props} result={locked} />); fireEvent.change(screen.getByLabelText("Test end date"), { target: { value: "2026-11-02" } }); fireEvent.click(screen.getByRole("button", { name: "Review test plan change" }));
  const dialog = await screen.findByRole("alertdialog"); expect(changeLockedPlan).not.toHaveBeenCalled(); const confirm = within(dialog).getByRole("button", { name: "Record test plan change" }); expect(confirm.matches(":disabled")).toBe(true);
  fireEvent.change(within(dialog).getByLabelText("Why does the test plan need to change?"), { target: { value: "Market closure" } }); fireEvent.click(confirm);
  await within(dialog).findByText("Explain the closure"); expect((within(dialog).getByLabelText("Why does the test plan need to change?") as HTMLTextAreaElement).value).toBe("Market closure"); expect((changeLockedPlan.mock.calls[0][2] as FormData).get("reason")).toBe("Market closure");
});
test("read-only viewers have no mutation controls and recalculating plans cannot change", () => {
  const view = render(<RegionsPlan {...props} canEdit={false} />); expect(screen.queryByRole("checkbox")).toBeNull(); view.rerender(<RegionsPlan {...props} result={{ ...proof, updating: true }} />); expect(screen.getByRole("checkbox", { name: /Pune/ }).matches(":disabled")).toBe(true);
});
test("unsaved brief and holdout edits block conflicting operations across the page", () => {
  render(<BriefWorkspace detail={detail} room={draftRoom} proof={proof} brandId="b" canEdit timeZone="Asia/Kolkata" now={0}><RegionsPlan {...props} /></BriefWorkspace>);
  choose(); expect(screen.getByRole("button", { name: "Save brief" }).matches(":disabled")).toBe(true); expect(screen.getByRole("button", { name: "Send for approval" }).matches(":disabled")).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Discard choices" })); fireEvent.change(screen.getByLabelText("Title"), { target: { value: "My draft" } }); expect(screen.getByRole("checkbox", { name: /Pune/ }).matches(":disabled")).toBe(true);
});
