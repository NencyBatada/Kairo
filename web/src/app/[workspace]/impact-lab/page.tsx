import { FlaskConical } from "lucide-react";
import type { Metadata } from "next";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

export const metadata: Metadata = { title: "Impact Lab" };

export default function ImpactLabPage() {
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Impact Lab" description="Measure changes Kairo didn't launch." />
      <EmptyState
        icon={FlaskConical}
        title="No analyses yet"
        description="Describe a change such as a price cut or a TV burst, pick an outcome, and Kairo estimates its effect against data the change didn't touch."
      />
    </div>
  );
}
