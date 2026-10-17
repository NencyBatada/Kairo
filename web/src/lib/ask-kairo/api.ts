import { REQUEST_HEADER } from "@/lib/http-names";
import { isConversation, isDetail, isHistory, isId, isMessage, isProposal, isTarget, type Question } from "./model";

export class AskError extends Error {
  constructor(message: string, public status = 0, public uncertain = false) { super(message); }
}
async function request<T>(path: string, validate: (value: unknown) => value is T, body?: unknown) {
  const mutation = body !== undefined;
  let response: Response;
  try { response = await fetch(path, { method: mutation ? "POST" : "GET", credentials: "same-origin", cache: "no-store", redirect: "error", headers: { [REQUEST_HEADER]: "1", ...(mutation ? { "Content-Type": "application/json" } : {}) }, ...(mutation ? { body: JSON.stringify(body) } : {}) }); }
  catch { throw new AskError("The connection was interrupted. Refresh the saved conversation before retrying.", 0, mutation); }
  const value: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const title = value && typeof value === "object" && "title" in value && typeof value.title === "string" ? value.title : null;
    throw new AskError(response.status === 401 ? "Sign in again to continue. Your draft is kept here." : response.status === 403 ? "Your current workspace access does not allow this action." : response.status === 404 ? "This conversation or item is no longer available." : response.status < 500 && title ? title : "Kairo could not complete this request. Refresh and try again.", response.status, mutation && response.status >= 500);
  }
  if (!validate(value)) throw new AskError("Kairo returned an incomplete response. Refresh the saved conversation before retrying.", 0, mutation);
  return value;
}
function base(brandId: string) { if (!isId(brandId)) throw new AskError("Choose a valid workspace."); return `/api/brands/${brandId}/ask/conversations`; }
function conversationPath(brandId: string, conversationId: string) { if (!isId(conversationId)) throw new AskError("Choose a valid conversation."); return `${base(brandId)}/${conversationId}`; }
function proposalPath(brandId: string, conversationId: string, proposalId: string) { if (!isId(proposalId)) throw new AskError("Choose a valid preview."); return `${conversationPath(brandId, conversationId)}/proposals/${proposalId}`; }
export const history = async (brandId: string) => request(base(brandId), isHistory);
export const create = async (brandId: string) => request(base(brandId), isConversation, {});
export async function detail(brandId: string, id: string) {
  const result = await request(conversationPath(brandId, id), isDetail);
  if (result.conversation.id !== id) throw new AskError("The returned conversation does not match. Refresh and try again.");
  return result;
}
export async function send(brandId: string, conversationId: string, question: Question) {
  if (!isId(question.requestId) || !isTarget(question.context) || !question.text.trim() || question.text.length > 4000) throw new AskError("Write a question of 1 to 4,000 characters and choose a supported item.");
  const message = await request(`${conversationPath(brandId, conversationId)}/messages`, isMessage, question);
  if (message.requestId.toLowerCase() !== question.requestId.toLowerCase() || message.question !== question.text.trim() || message.context.kind !== question.context.kind || (message.context.id?.toLowerCase() ?? null) !== (question.context.id?.toLowerCase() ?? null) || (message.context.campaignId?.toLowerCase() ?? null) !== (question.context.campaignId?.toLowerCase() ?? null)) throw new AskError("The returned answer does not match. Refresh the saved conversation before retrying.", 0, true);
  return message;
}
export async function proposal(brandId: string, conversationId: string, proposalId: string) {
  const result = await request(proposalPath(brandId, conversationId, proposalId), isProposal);
  if (result.id !== proposalId) throw new AskError("The returned preview does not match. Refresh and try again.");
  return result;
}
export async function decide(brandId: string, conversationId: string, proposalId: string, decision: "confirm" | "reject") {
  const result = await request(`${proposalPath(brandId, conversationId, proposalId)}/${decision}`, isProposal, {});
  if (result.id !== proposalId) throw new AskError("The returned preview does not match. Refresh before trying again.", 0, true);
  return result;
}
