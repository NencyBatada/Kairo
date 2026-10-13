import { Palette } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Brand Kit" };

export default function BrandKitPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Brand Kit" description="Your brand's voice, visuals, rules and memory, used by every agent." />
      <EmptyState
        icon={Palette}
        title="No Brand Kit yet"
        description="Kairo will draft your tone of voice, colours, logo, key messages and product categories from your website and best ads, for an admin to approve."
      />
    </div>
  );
}
