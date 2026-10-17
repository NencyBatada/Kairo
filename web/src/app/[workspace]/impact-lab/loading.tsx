import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() { return <div role="status" aria-label="Loading Impact Lab" className="space-y-6"><Skeleton className="h-24 w-full" /><Skeleton className="h-20 w-full" /><Skeleton className="h-96 w-full" /></div>; }
