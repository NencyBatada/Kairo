import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return <div role="status" aria-label="Loading recommendations" className="space-y-6"><Skeleton className="h-24 w-full" /><Skeleton className="h-28 w-full" /><Skeleton className="h-96 w-full" /><Skeleton className="h-60 w-full" /></div>;
}
