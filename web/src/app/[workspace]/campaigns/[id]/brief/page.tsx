import { FileText } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function BriefPage() {
  return (
    <EmptyState
      icon={FileText}
      title="No brief yet"
      description="Kairo drafts the brief here, with the reasoning for each field, the budget against your limit, and the test and holdout regions."
    />
  );
}
