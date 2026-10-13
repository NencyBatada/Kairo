import { Compass } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";

export function NotFoundState() {
  return (
    <EmptyState as="h1" icon={Compass} title="We couldn't find that page" description="It may have moved, or you may not have access to it.">
      <Link href="/" className={buttonVariants()}>
        Go to Kairo
      </Link>
    </EmptyState>
  );
}
