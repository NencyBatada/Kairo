import { TrendingUp } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Opportunities" };

export default function OpportunitiesPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Opportunities" description="Rising demand Kairo found for your products, ranked by score." />
      <EmptyState
        icon={TrendingUp}
        title="No opportunities yet"
        description="Every morning before 07:00, Kairo will rank rising searches you can sell into, with the trend, regions, matching products and the reasons behind each score."
      />
    </div>
  );
}
