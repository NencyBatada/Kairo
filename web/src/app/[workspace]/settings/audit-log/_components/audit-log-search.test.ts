import { expect, test } from "vitest";

import { auditLogQuery, coreAuditLogQuery, hasFilters, parseAuditLogSearch } from "./audit-log-search";

test("keeps valid filters and the cursor", () => {
  const { search, before } = parseAuditLogSearch({
    action: ["member.removed", "brand.updated", "member.removed", ""],
    actor: "  Neha ",
    actorType: "agent",
    from: "2026-10-01",
    to: "2026-10-14",
    before: "42",
  });

  expect(search).toEqual({ actions: ["member.removed", "brand.updated"], actor: "Neha", actorType: "agent", from: "2026-10-01", to: "2026-10-14" });
  expect(before).toBe("42");
  expect(hasFilters(search)).toBe(true);
});

test("drops values core would reject", () => {
  const { search, before } = parseAuditLogSearch({
    actorType: "robot",
    from: "yesterday",
    to: "2026-02-30",
    before: "abc",
  });

  expect(search).toEqual({ actions: [], actor: undefined, actorType: undefined, from: undefined, to: undefined });
  expect(before).toBeUndefined();
  expect(hasFilters(search)).toBe(false);
  expect(parseAuditLogSearch({ before: "-5" }).before).toBeUndefined();
  expect(parseAuditLogSearch({ before: "0" }).before).toBeUndefined();
  expect(parseAuditLogSearch({ before: "99999999999999999999" }).before).toBeUndefined();
});

test("builds page links that keep the filters", () => {
  const search = { actions: ["member.removed", "brand.updated"], actor: "Neha Rao", actorType: "user" as const, from: "2026-10-01" };

  expect(auditLogQuery(search, "7")).toBe("?action=member.removed&action=brand.updated&actor=Neha+Rao&actorType=user&from=2026-10-01&before=7");
  expect(auditLogQuery({ actions: [] })).toBe("");
});

test("passes the filters to core", () => {
  expect(coreAuditLogQuery({ actions: [] })).toEqual({ action: undefined, actor: undefined, actorType: undefined, from: undefined, to: undefined });
  expect(coreAuditLogQuery({ actions: ["a.b"], to: "2026-10-14" }).action).toEqual(["a.b"]);
});
