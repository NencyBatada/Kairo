import { Megaphone } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Campaigns" };

export default function CampaignsPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Campaigns" description="Every campaign room, from brief to results." />
      <EmptyState
        icon={Megaphone}
        title="No campaigns yet"
        description="Accept an opportunity and Kairo opens a campaign room for it, with the evidence attached and the brief drafting."
      />
    </div>
  );
}
