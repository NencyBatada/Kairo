import type { AuditActorType } from "@/lib/core-api/types";

export type AuditLogSearch = {
  actions: string[];
  actor?: string;
  actorType?: AuditActorType;
  from?: string;
  to?: string;
};

type SearchParams = Record<string, string | string[] | undefined>;

export const actorTypeLabels: Record<AuditActorType, string> = {
  user: "Person",
  agent: "Agent",
  system: "System",
};

const actorTypes = Object.keys(actorTypeLabels) as AuditActorType[];
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
const cursorPattern = /^[1-9]\d{0,18}$/;

function values(value: string | string[] | undefined) {
  return (Array.isArray(value) ? value : value === undefined ? [] : [value]).map((item) => item.trim()).filter(Boolean);
}

function first(value: string | string[] | undefined) {
  return values(value)[0];
}

function isDate(value: string | undefined): value is string {
  if (!value || !datePattern.test(value)) {
    return false;
  }

  const date = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(value);
}

export function parseAuditLogSearch(query: SearchParams): { search: AuditLogSearch; before?: string } {
  const actor = first(query.actor)?.slice(0, 200);
  const actorType = first(query.actorType);
  const from = first(query.from);
  const to = first(query.to);
  const before = first(query.before);

  return {
    search: {
      actions: [...new Set(values(query.action))].slice(0, 50),
      actor,
      actorType: actorTypes.includes(actorType as AuditActorType) ? (actorType as AuditActorType) : undefined,
      from: isDate(from) ? from : undefined,
      to: isDate(to) ? to : undefined,
    },
    before: before && cursorPattern.test(before) ? before : undefined,
  };
}

export function hasFilters(search: AuditLogSearch) {
  return search.actions.length > 0 || Boolean(search.actor || search.actorType || search.from || search.to);
}

export function auditLogQuery(search: AuditLogSearch, before?: string) {
  const params = new URLSearchParams();
  for (const action of search.actions) params.append("action", action);
  if (search.actor) params.set("actor", search.actor);
  if (search.actorType) params.set("actorType", search.actorType);
  if (search.from) params.set("from", search.from);
  if (search.to) params.set("to", search.to);
  if (before) params.set("before", before);
  return params.size > 0 ? `?${params}` : "";
}

export function coreAuditLogQuery(search: AuditLogSearch) {
  return {
    action: search.actions.length > 0 ? search.actions : undefined,
    actor: search.actor,
    actorType: search.actorType,
    from: search.from,
    to: search.to,
  };
}
