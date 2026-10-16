import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";

import { detail, draftRoom, proof, proposal } from "../fixtures.test-data";
import { BriefWorkspace } from "./brief-workspace";
import { ProofSummary } from "./proof-summary";

const { saveBrief, proposeBrief, applyBrief, requestBriefApproval, decideBrief } = vi.hoisted(() => ({ saveBrief: vi.fn(), proposeBrief: vi.fn(), applyBrief: vi.fn(), requestBriefApproval: vi.fn(), decideBrief: vi.fn() }));
vi.mock("../actions", () => ({ saveBrief, proposeBrief, applyBrief, requestBriefApproval, decideBrief }));
const now = Date.parse("2026-10-16T01:00:00Z");
const props = { detail, room: draftRoom, proof, brandId: "brand-1", canEdit: true, timeZone: "Asia/Kolkata", now, children: <ProofSummary result={proof} /> };
beforeEach(() => { vi.clearAllMocks(); saveBrief.mockResolvedValue({ status: "saved", message: "Saved", detail }); proposeBrief.mockResolvedValue({ status: "saved", message: "Preview ready", proposal }); applyBrief.mockResolvedValue({ status: "saved", message: "Applied", detail }); requestBriefApproval.mockResolvedValue({ status: "saved", message: "Requested" }); decideBrief.mockResolvedValue({ status: "saved", message: "Approved" }); });

test("shows every editable field, reasoning, test numbers and localized history", () => {
  render(<BriefWorkspace {...props} />);
  expect(screen.getByLabelText("Title")).toBeTruthy(); expect(screen.getByLabelText("Start date")).toBeTruthy(); expect(screen.getByRole("group", { name: "Products" })).toBeTruthy(); expect(screen.getByLabelText("Landing pages · one HTTPS address per line")).toBeTruthy();
  expect(screen.getByText("Based on keyword bid ranges.")).toBeTruthy(); expect(screen.getByRole("link", { name: "Source" }).getAttribute("href")).toBe("https://example.com/keywords");
  expect(screen.getByText("12.0%")).toBeTruthy(); expect(screen.getByText("₹1,200.00")).toBeTruthy(); expect(screen.getByText(/6:00 am/i)).toBeTruthy();
});
test("unsaved edits block instructions and approval; discard restores saved values", async () => {
  render(<BriefWorkspace {...props} />);
  fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Unsaved" } });
  expect((screen.getByRole("button", { name: "Preview changes" }) as HTMLButtonElement).disabled).toBe(true); expect((screen.getByRole("button", { name: "Send for approval" }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Discard edits" }));
  await waitFor(() => expect((screen.getByLabelText("Title") as HTMLInputElement).value).toBe(detail.brief!.title));
  expect((screen.getByRole("button", { name: "Preview changes" }) as HTMLButtonElement).disabled).toBe(false);
});
test("background changes preserve unsaved values and require an explicit reload", () => {
  const view = render(<BriefWorkspace {...props} />);
  fireEvent.change(screen.getByLabelText("Title"), { target: { value: "My work" } });
  view.rerender(<BriefWorkspace {...props} detail={{ ...detail, campaignVersion: 2, brief: { ...detail.brief!, version: 3, title: "Someone else's change" } }} />);
  expect((screen.getByLabelText("Title") as HTMLInputElement).value).toBe("My work"); expect(screen.getByRole("button", { name: "Save brief" }).matches(":disabled")).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Discard edits and load latest" })); expect((screen.getByLabelText("Title") as HTMLInputElement).value).toBe("Someone else's change");
});
test("untouched brief fields follow a new saved version without blocking editing", () => {
  const view = render(<BriefWorkspace {...props} />);
  view.rerender(<BriefWorkspace {...props} detail={{ ...detail, campaignVersion: 2, brief: { ...detail.brief!, version: 3, title: "Latest saved brief" } }} room={{ ...draftRoom, version: 2 }} />);
  expect((screen.getByLabelText("Title") as HTMLInputElement).value).toBe("Latest saved brief");
  expect(screen.getByRole("button", { name: "Save brief" }).matches(":disabled")).toBe(false);
  expect(screen.queryByText(/newer campaign version is available/)).toBeNull();
});
test("failed edits retain entered values and focus a linked error summary", async () => {
  saveBrief.mockResolvedValue({ status: "error", message: "Check the budget", fieldErrors: { budget: ["Too large"] } });
  render(<BriefWorkspace {...props} />); fireEvent.change(screen.getByLabelText("Title"), { target: { value: "My work" } });
  fireEvent.submit(screen.getByRole("button", { name: "Save brief" }).closest("form")!);
  await screen.findByText("Too large", { selector: "a" }); expect((screen.getByLabelText("Title") as HTMLInputElement).value).toBe("My work"); expect(document.activeElement?.textContent).toMatch(/Check the budget/);
});
test("instructions show before and after, never applying until explicitly confirmed", async () => {
  render(<BriefWorkspace {...props} />); fireEvent.change(screen.getByLabelText("What should change?"), { target: { value: "Add Hindi" } });
  fireEvent.submit(screen.getByRole("button", { name: "Preview changes" }).closest("form")!);
  await screen.findByRole("heading", { name: "Proposed changes" }); expect(screen.getAllByText("English, Hindi").length).toBeGreaterThan(0); expect(applyBrief).not.toHaveBeenCalled();
  await waitFor(() => expect((screen.getByRole("button", { name: "Apply changes" }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole("button", { name: "Apply changes" })); await waitFor(() => expect(applyBrief).toHaveBeenCalledWith("brand-1", draftRoom.id, proposal.id, "1"));
  await waitFor(() => expect(screen.queryByRole("heading", { name: "Proposed changes" })).toBeNull());
});
test("a failed apply preserves its preview and a changed version disables it", async () => {
  applyBrief.mockResolvedValue({ status: "error", message: "The service is busy" });
  const view = render(<BriefWorkspace {...props} />); fireEvent.submit(screen.getByRole("button", { name: "Preview changes" }).closest("form")!); await screen.findByRole("button", { name: "Apply changes" });
  await waitFor(() => expect((screen.getByRole("button", { name: "Apply changes" }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole("button", { name: "Apply changes" })); await screen.findByText("The service is busy"); expect(screen.getAllByText("English, Hindi").length).toBeGreaterThan(0);
  view.rerender(<BriefWorkspace {...props} detail={{ ...detail, campaignVersion: 2 }} />);
  expect((screen.getByRole("button", { name: "Apply changes" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByText(/campaign changed after this preview/)).toBeTruthy();
});
test("expired previews cannot be applied", async () => {
  proposeBrief.mockResolvedValue({ status: "saved", proposal: { ...proposal, expiresAt: "2026-10-16T00:59:00Z" } });
  render(<BriefWorkspace {...props} />); fireEvent.submit(screen.getByRole("button", { name: "Preview changes" }).closest("form")!);
  await screen.findByText(/preview has expired/); expect((screen.getByRole("button", { name: "Apply changes" }) as HTMLButtonElement).disabled).toBe(true);
});
test("viewers see saved values and reasoning without editing controls", () => {
  render(<BriefWorkspace {...props} canEdit={false} />); expect(screen.queryByRole("button", { name: "Save brief" })).toBeNull(); expect(screen.queryByLabelText("What should change?")).toBeNull(); expect(screen.getByText(detail.brief!.objective)).toBeTruthy();
});
test("pending approval is read-only and shows budget-owner routing and exact summary", async () => {
  const room = { ...draftRoom, pendingApproval: { gate: "brief", label: "Brief", requestedAt: "2026-10-16T01:00:00Z", requestedBy: null, route: "budget_owner", routeReason: "Only the budget owner can approve.", approvers: [{ id: "a", name: "Nency" }] }, actions: { ...draftRoom.actions, approve: "brief", sendBackTo: ["brief_draft"] } };
  render(<BriefWorkspace {...props} room={room} detail={{ ...detail, editable: false }} />);
  expect(screen.queryByRole("button", { name: "Save brief" })).toBeNull(); expect(screen.getByText("Only the budget owner can approve.")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Approve brief" })); const dialog = await screen.findByRole("dialog"); expect(decideBrief).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "Approve brief" })); await waitFor(() => expect(decideBrief).toHaveBeenCalledWith("brand-1", draftRoom.id, "1", { kind: "approve" }));
});
test("proof summary handles missing data, recalculation and locked changes", () => {
  render(<ProofSummary result={{ ...proof, updating: true, plan: { ...proof.plan!, minDetectableLift: null, holdoutCost: null, lockedAt: "2026-10-16T01:00:00Z", changedAfterLock: true, approvedWithWarnings: true, changeReason: "Market closed", warnings: [{ code: "thin", message: "Not enough history" }] } }} />);
  expect(screen.getByText(/recalculating/)).toBeTruthy(); expect(screen.getByText("Locked at launch")).toBeTruthy(); expect(screen.getAllByText("Not available")).toHaveLength(2); expect(screen.getByText(/Market closed/)).toBeTruthy(); expect(screen.getByText("Not enough history")).toBeTruthy();
});
