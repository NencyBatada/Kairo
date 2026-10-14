export const auditActionLabels: Record<string, string> = {
  "brand.created": "Created the workspace",
  "brand.updated": "Changed the workspace details",
  "invitation.sent": "Sent an invitation",
  "invitation.revoked": "Revoked an invitation",
  "invitation.accepted": "Accepted an invitation",
  "invitation.declined": "Declined an invitation",
  "member.roles_changed": "Changed a member's roles",
  "member.removed": "Removed a member",
  "member.left": "Left the workspace",
  "approval_settings.changed": "Changed the approval settings",
  "connection.connected": "Connected an account",
  "connection.account_chosen": "Chose an account",
  "connection.disconnected": "Disconnected an account",
  "notifications.email_settings_changed": "Changed notification email settings",
};

export function auditActionLabel(action: string) {
  return auditActionLabels[action] ?? action;
}

export const auditActionOptions = Object.entries(auditActionLabels).map(([value, label]) => ({ value, label }));
