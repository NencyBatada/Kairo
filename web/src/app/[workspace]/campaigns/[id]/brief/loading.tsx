import { Skeleton } from "@/components/ui/skeleton";

export default function LoadingBrief() {
  return <div role="status" aria-label="Loading brief" className="space-y-6"><span className="sr-only">Loading brief and test plan</span><Skeleton className="h-8 w-64" />{[0, 1, 2].map(item => <div key={item} className="space-y-4 rounded-lg border p-6"><Skeleton className="h-6 w-40" /><Skeleton className="h-24 w-full" /><Skeleton className="h-10 w-full" /></div>)}</div>;
}
