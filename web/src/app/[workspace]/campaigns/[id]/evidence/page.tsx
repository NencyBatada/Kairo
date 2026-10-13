import { FileSearch } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function EvidencePage() {
  return (
    <EmptyState icon={FileSearch} title="No evidence yet" description="The opportunity's trend, regions, matching products and sources will be attached here." />
  );
}
