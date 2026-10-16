import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return <div role="status" aria-label="Loading performance" className="space-y-6"><Skeleton className="h-20 w-full" /><div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{[1, 2, 3, 4].map(id => <Skeleton key={id} className="h-28 w-full" />)}</div><Skeleton className="h-80 w-full" /><Skeleton className="h-48 w-full" /></div>;
}
