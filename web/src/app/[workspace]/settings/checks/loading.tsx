import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return <div role="status" aria-label="Loading check settings" className="space-y-6"><Skeleton className="h-24 w-full" /><Skeleton className="h-32 w-full max-w-160" /><Skeleton className="h-96 w-full" /></div>;
}
