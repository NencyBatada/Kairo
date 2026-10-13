import { Images } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function AssetsPage() {
  return (
    <EmptyState
      icon={Images}
      title="No assets yet"
      description="Search ads, Demand Gen ads, images and the video will appear here once the brief is approved, each with its Brand Guardian score."
    />
  );
}
