import { Clock, TimerOff } from "lucide-react";

import { Badge } from "@/components/ui/badge";

export type ApprovalStatus = "waiting" | "approved" | "sent-back" | "expired";

const statuses = {
  waiting: { variant: "gate", label: "Waiting for approval", Icon: Clock },
  approved: { variant: "success", label: "Approved", Icon: null },
  "sent-back": { variant: "warning", label: "Sent back", Icon: null },
  expired: { variant: "neutral", label: "Expired", Icon: TimerOff },
} as const;

export function ApprovalStatusBadge({ status }: { status: ApprovalStatus }) {
  const { variant, label, Icon } = statuses[status];

  return (
    <Badge variant={variant} data-status={status}>
      {Icon && <Icon aria-hidden="true" />}
      {label}
    </Badge>
  );
}
