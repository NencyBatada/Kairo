import { ChartLine } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Opportunity" };

export default function OpportunityPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader eyebrow="Opportunities" title="Opportunity" description="The evidence behind one opportunity." />
      <EmptyState
        icon={ChartLine}
        title="Nothing to show yet"
        description="The search trend, rising regions, matching products, sourced reasons and the score breakdown will appear here."
      />
    </div>
  );
}
