import { Rocket } from "lucide-react";

import { EmptyState } from "@/components/empty-state";

export default function LaunchPage() {
  return (
    <EmptyState
      icon={Rocket}
      title="Not ready to launch"
      description="The launch summary, its checks and live progress will appear here after the assets are approved."
    />
  );
}
