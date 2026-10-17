import type { components } from "@/lib/core-api/schema";
import type { Role } from "@/lib/core-api/types";

type Schemas = components["schemas"];
export type Target = Schemas["AskTarget"];
export type Conversation = Schemas["ConversationView"];
export type Detail = Schemas["ConversationDetail"];
export type Message = Schemas["MessageView"];
export type Proposal = Schemas["ProposalView"];
export type Segment = Schemas["AnswerSegment"];
export type Evidence = Schemas["Evidence"];
export type Question = Schemas["SendMessage"];
export const ASK_EVENTS = ["ask.answer.segment", "ask.message.completed", "ask.message.failed", "ask.proposal.changed"] as const;
export type AskEventName = typeof ASK_EVENTS[number];
export type AskEvent = { type: AskEventName; conversationId: string; messageId?: string; index?: number; segment?: Segment; message?: Message; proposal?: Proposal };
export const isId = (value: unknown): value is string => typeof value === "string" && /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value) && !/^0{8}(-0{4}){3}-0{12}$/.test(value);
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(item => typeof item === "string");
const date = (value: unknown) => typeof value === "string" && Number.isFinite(Date.parse(value));
const nullableText = (value: unknown) => value === null || typeof value === "string";
const kinds = ["workspace", "campaign", "opportunity", "brief", "asset", "report", "impact_lab", "brand_kit", "catalogue", "trigger_rule", "brand_memory"];

export function isTarget(value: unknown): value is Target {
  return object(value) && typeof value.kind === "string" && kinds.includes(value.kind) &&
    (["workspace", "brand_kit"].includes(value.kind) || isId(value.id)) &&
    (value.kind !== "asset" || isId(value.campaignId));
}
export const isConversation = (value: unknown): value is Conversation => object(value) && isId(value.id) && typeof value.title === "string" && date(value.updatedAt);
export const isSegment = (value: unknown): value is Segment => object(value) && typeof value.text === "string" && strings(value.evidenceIds);
export const isEvidence = (value: unknown): value is Evidence => object(value) && typeof value.id === "string" && isTarget(value.target) && typeof value.title === "string" && typeof value.path === "string" && (value.sources == null || strings(value.sources));
export function isMessage(value: unknown): value is Message {
  return object(value) && isId(value.id) && isId(value.requestId) && typeof value.question === "string" && isTarget(value.context) &&
    ["running", "completed", "failed"].includes(String(value.status)) && Array.isArray(value.segments) && value.segments.every(isSegment) &&
    Array.isArray(value.evidence) && value.evidence.every(isEvidence) && nullableText(value.error) && date(value.createdAt) && nullableText(value.model) && nullableText(value.promptVersion);
}
export function isProposal(value: unknown): value is Proposal {
  return object(value) && isId(value.id) && isId(value.messageId) && isTarget(value.target) &&
    ["queued", "generating", "ready", "applied", "rejected", "failed"].includes(String(value.status)) && strings(value.warnings) &&
    Array.isArray(value.files) && value.files.every(file => object(file) && typeof file.name === "string" && typeof file.url === "string" && typeof file.contentType === "string") &&
    nullableText(value.error) && date(value.expiresAt) && (value.appliedAt === null || date(value.appliedAt)) && "before" in value && "after" in value;
}
export const isDetail = (value: unknown): value is Detail => object(value) && isConversation(value.conversation) && Array.isArray(value.messages) && value.messages.every(isMessage) && Array.isArray(value.proposals) && value.proposals.every(isProposal);
export const isHistory = (value: unknown): value is Conversation[] => Array.isArray(value) && value.every(isConversation);

export function parseAskEvent(type: AskEventName, data: string): AskEvent | null {
  try {
    const value: unknown = JSON.parse(data);
    if (!object(value) || !isId(value.conversationId)) return null;
    if (type === "ask.answer.segment" && (!isId(value.messageId) || !Number.isInteger(value.index) || Number(value.index) < 0 || Number(value.index) > 11 || !isSegment(value.segment))) return null;
    if ((type === "ask.message.completed" || type === "ask.message.failed") && !isMessage(value.message)) return null;
    if (type === "ask.proposal.changed" && !isProposal(value.proposal)) return null;
    return { ...value, type } as AskEvent;
  } catch { return null; }
}

export function pageTarget(pathname: string, handle: string): Target {
  const parts = pathname.split("/").filter(Boolean);
  if (parts[0] !== handle) return { kind: "workspace" };
  const [, area, id, child, childId] = parts;
  if (area === "campaigns" && isId(id)) return { kind: child === "brief" ? "brief" : child === "results" ? "report" : "campaign", id };
  if (area === "opportunities" && isId(id)) return { kind: "opportunity", id };
  if (area === "impact-lab" && isId(id)) return { kind: "impact_lab", id };
  if (area === "triggers" && isId(id)) return { kind: "trigger_rule", id };
  if (area === "brand-kit" && !id) return { kind: "brand_kit" };
  if (area === "brand-kit" && id === "memory" && isId(child)) return { kind: "brand_memory", id: child };
  if (area === "brand-kit" && id === "catalogue" && child === "products" && isId(childId)) return { kind: "catalogue", id: childId };
  return { kind: "workspace" };
}

export const targetLabels: Record<string, string> = { workspace: "Workspace", campaign: "Campaign", opportunity: "Opportunity", brief: "Smart Brief", asset: "Asset", report: "Impact report", impact_lab: "Impact Lab analysis", brand_kit: "Brand Kit draft", catalogue: "Catalogue product", trigger_rule: "Trigger rule", brand_memory: "Brand Memory lesson" };
export function targetPath(target: Target, handle: string) {
  if (!isTarget(target)) return null;
  const base = `/${encodeURIComponent(handle)}`;
  const paths: Record<string, string> = { workspace: "today", campaign: `campaigns/${target.id}`, brief: `campaigns/${target.id}/brief`, asset: `campaigns/${target.campaignId}/assets#asset-${target.id}`, report: `campaigns/${target.id}/results`, opportunity: `opportunities/${target.id}`, impact_lab: `impact-lab/${target.id}`, brand_kit: "brand-kit", catalogue: `brand-kit/catalogue/products/${target.id}`, trigger_rule: `triggers/${target.id}`, brand_memory: `brand-kit/memory/${target.id}` };
  return `${base}/${paths[target.kind]}`;
}

export function evidencePath(evidence: Evidence, handle: string) {
  const expected = targetPath(evidence.target, handle);
  return evidence.path === expected || evidence.target.kind === "asset" && evidence.path === expected?.split("#")[0] ? evidence.path : null;
}
export function sourceUrl(value: string) {
  try { const url = new URL(value); return ["https:", "http:"].includes(url.protocol) && !url.username && !url.password ? url.href : null; } catch { return null; }
}
export function previewUrl(value: string, brandId: string, conversationId: string, proposalId: string) {
  const prefix = `/api/brands/${brandId}/ask/conversations/${conversationId}/proposals/${proposalId}/files/`;
  if (!value.startsWith(prefix)) return null;
  try { const name = decodeURIComponent(value.slice(prefix.length)); return name && !/[\/\\?#\x00-\x1f]/.test(name) && name !== "." && name !== ".." ? value : null; } catch { return null; }
}
export function canApply(target: Target, roles: Role[]) {
  if (["brief", "asset"].includes(target.kind)) return roles.includes("marketer");
  if (["brand_kit", "trigger_rule"].includes(target.kind)) return roles.includes("admin");
  return ["brand_memory", "catalogue"].includes(target.kind) && roles.some(role => role === "admin" || role === "marketer");
}
export function proposalReady(proposal: Proposal, now: number) {
  return proposal.status === "ready" && proposal.before !== null && proposal.after !== null && Date.parse(proposal.expiresAt) > now;
}
export function mergeMessage(previous: Message | undefined, next: Message): Message {
  if (!previous) return next;
  if (previous.status !== "running" && next.status === "running") return previous;
  const segments = [...previous.segments];
  next.segments.forEach((segment, index) => { segments[index] = segment; });
  return { ...next, segments, evidence: next.evidence.length < previous.evidence.length ? previous.evidence : next.evidence };
}
export function mergeDetail(previous: Detail | null, next: Detail): Detail {
  if (!previous || previous.conversation.id !== next.conversation.id) return next;
  const messages = next.messages.map(message => mergeMessage(previous.messages.find(item => item.id === message.id || item.requestId === message.requestId), message));
  for (const message of previous.messages) if (message.status === "running" && !messages.some(item => item.requestId === message.requestId)) messages.push(message);
  const proposals = next.proposals.map(proposal => mergeProposal(previous.proposals.find(item => item.id === proposal.id), proposal));
  for (const proposal of previous.proposals) if (!proposals.some(item => item.id === proposal.id)) proposals.push(proposal);
  return { ...next, messages, proposals };
}
export function mergeProposal(previous: Proposal | undefined, next: Proposal) {
  const stages = ["queued", "generating", "ready", "failed", "rejected", "applied"];
  return previous && stages.indexOf(previous.status) > stages.indexOf(next.status) ? previous : next;
}
export function withMessage(detail: Detail, message: Message): Detail {
  const exists = detail.messages.some(item => item.id === message.id || item.requestId === message.requestId);
  return { ...detail, messages: exists ? detail.messages.map(item => item.id === message.id || item.requestId === message.requestId ? mergeMessage(item, message) : item) : [...detail.messages, message] };
}
export function applyEvent(detail: Detail, event: AskEvent): Detail {
  if (detail.conversation.id !== event.conversationId) return detail;
  if (event.message) return withMessage(detail, event.message);
  if (event.proposal) return { ...detail, proposals: [...detail.proposals.filter(item => item.id !== event.proposal!.id), mergeProposal(detail.proposals.find(item => item.id === event.proposal!.id), event.proposal)] };
  if (event.segment && event.messageId && event.index !== undefined) {
    const message = detail.messages.find(item => item.id === event.messageId) ?? detail.messages.find(item => item.status === "running" && item.id === item.requestId);
    if (!message || message.status !== "running") return detail;
    const segments = [...message.segments];
    segments[event.index] = event.segment;
    return withMessage(detail, { ...message, id: event.messageId, segments });
  }
  return detail;
}
export function displayValue(value: unknown): string {
  if (value == null) return "Not set";
  if (typeof value === "string") return value || "Empty";
  if (Array.isArray(value)) return value.length ? value.map(displayValue).join("\n") : "None";
  if (object(value)) return Object.entries(value).map(([key, item]) => `${fieldLabel(key)}: ${displayValue(item)}`).join("\n");
  return String(value);
}
export const fieldLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2").replaceAll("_", " ").replace(/^./, character => character.toUpperCase());
export function changedFields(before: unknown, after: unknown) {
  if (!object(before) || !object(after)) return [{ name: "Content", before: displayValue(before), after: displayValue(after) }];
  return [...new Set([...Object.keys(before), ...Object.keys(after)])].filter(key => JSON.stringify(before[key]) !== JSON.stringify(after[key])).map(key => ({ name: fieldLabel(key), before: displayValue(before[key]), after: displayValue(after[key]) }));
}
