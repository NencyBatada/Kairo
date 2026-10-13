import { Sun } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Today" };

export default function TodayPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Today" description="What needs you this morning." />
      <EmptyState
        icon={Sun}
        title="Nothing needs you yet"
        description="Approvals waiting for you, alerts, top opportunities and live tests will show up here each morning."
      />
    </div>
  );
}
