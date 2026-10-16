import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() { return <div role="status" aria-label="Loading launch" className="space-y-6"><Skeleton className="h-10 w-40" /><Skeleton className="h-24 w-full" /><div className="grid gap-4 sm:grid-cols-2">{[0, 1, 2, 3].map(value => <Skeleton key={value} className="h-28 w-full" />)}</div><Skeleton className="h-64 w-full" /><span className="sr-only">Loading the saved launch plan and progress.</span></div>; }
