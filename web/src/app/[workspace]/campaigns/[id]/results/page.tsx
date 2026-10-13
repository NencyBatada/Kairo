import { ChartColumn } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function ResultsPage() {
  return (
    <EmptyState
      icon={ChartColumn}
      title="No results yet"
      description="The impact report, with the lift, its range and the recommended next step, will appear here when the test ends."
    />
  );
}
