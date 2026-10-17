import type { Workspace } from "@/lib/core-api/types";
import type { Conversation, Detail, Evidence, Message, Proposal } from "./model";

export const brandId = "11111111-1111-1111-1111-111111111111", conversationId = "22222222-2222-2222-2222-222222222222", messageId = "33333333-3333-3333-3333-333333333333", proposalId = "44444444-4444-4444-4444-444444444444", itemId = "55555555-5555-5555-5555-555555555555", requestId = "66666666-6666-6666-6666-666666666666";
export const workspace: Workspace = { id: brandId, handle: "acme", name: "Acme", roles: ["marketer"] };
export const conversation: Conversation = { id: conversationId, title: "Explain this campaign", updatedAt: "2026-10-17T04:00:00Z" };
export const evidence: Evidence = { id: "e1", target: { kind: "campaign", id: itemId }, title: "Diwali campaign", path: `/acme/campaigns/${itemId}`, data: { title: "Diwali" }, sources: ["https://example.com/evidence"] };
export function message(overrides: Partial<Message> = {}): Message { return { id: messageId, requestId, question: "Explain this campaign", context: { kind: "campaign", id: itemId }, status: "completed", segments: [{ text: "Review the saved brief before approval.", evidenceIds: ["e1"] }], evidence: [evidence], error: null, createdAt: "2026-10-17T04:00:00Z", model: "gemini", promptVersion: "ask_v1", ...overrides }; }
export function proposal(overrides: Partial<Proposal> = {}): Proposal { return { id: proposalId, messageId, target: { kind: "brief", id: itemId }, status: "ready", before: { title: "Old brief", audiences: ["Parents"] }, after: { title: "New brief", audiences: ["Parents"] }, warnings: ["The updated brief needs its normal approval."], files: [], error: null, expiresAt: "2099-10-17T04:30:00Z", appliedAt: null, ...overrides }; }
export function detail(overrides: Partial<Detail> = {}): Detail { return { conversation, messages: [message()], proposals: [], ...overrides }; }
