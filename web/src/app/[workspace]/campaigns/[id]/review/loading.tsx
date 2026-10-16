import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return <div role="status" aria-label="Loading Brand Guardian" className="space-y-6"><Skeleton className="h-36 w-full" /><Skeleton className="h-16 w-full" /><div className="grid gap-4 lg:grid-cols-2"><Skeleton className="h-96 w-full" /><Skeleton className="h-96 w-full" /></div></div>;
}
