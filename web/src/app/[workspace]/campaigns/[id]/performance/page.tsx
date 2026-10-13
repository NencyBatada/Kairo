import { Activity } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function PerformancePage() {
  return (
    <EmptyState
      icon={Activity}
      title="No performance yet"
      description="Pacing against plan, daily spend and results, early readings and the pause log will appear here once the campaign is live."
    />
  );
}
