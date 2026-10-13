import { ListChecks } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function CampaignRoomPage() {
  return (
    <EmptyState
      icon={ListChecks}
      title="Nothing here yet"
      description="The stage tracker, who owns the next action, and the log of every agent and person's action will appear here."
    />
  );
}
