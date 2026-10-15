import type { components } from "./schema";

type Schemas = components["schemas"];

export type Role = Schemas["BrandRole"];
export type Workspace = Schemas["WorkspaceSummary"];
export type WorkspaceDetails = Schemas["WorkspaceDetails"];
export type MyInvitation = Schemas["MyInvitation"];
export type Member = Schemas["MemberResponse"];
export type PendingInvitation = Schemas["PendingInvitation"];
export type ApprovalSettings = Schemas["ApprovalSettings"];
export type AuditLogPage = Schemas["AuditLogPage"];
export type AuditEntry = Schemas["AuditEntryResponse"];
export type AuditActorType = Schemas["AuditActorType"];
export type AuditActionOption = Schemas["AuditActionOption"];
export type NotificationPage = Schemas["NotificationPage"];
export type NotificationItem = Schemas["NotificationItem"];
export type EmailSettings = Schemas["EmailSettings"];
export type ConnectionView = Schemas["ConnectionView"];
export type ProductAccount = Schemas["ProductAccount"];
export type CheckResult = Schemas["CheckResult"];

export const roleLabels: Record<Role, string> = {
  admin: "Admin",
  marketer: "Marketer",
  approver: "Approver",
};
