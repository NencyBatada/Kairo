import { Zap } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Triggers" };

export default function TriggersPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Triggers" description="Rules that turn weather, festivals and stock into opportunities ready to review." />
      <EmptyState
        icon={Zap}
        title="No trigger rules yet"
        description="A rule watches a forecast, a festival date or a stock level, and drafts an opportunity when its moment comes. Nothing spends money until a person approves it."
      />
    </div>
  );
}
