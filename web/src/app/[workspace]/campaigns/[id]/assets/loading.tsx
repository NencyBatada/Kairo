import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return <div role="status" aria-label="Loading campaign assets" className="grid gap-5"><Skeleton className="h-7 w-48" /><Skeleton className="h-10 w-full max-w-120" /><div className="grid gap-4 xl:grid-cols-2">{[0, 1, 2, 3].map((item) => <Skeleton key={item} className="h-80 rounded-lg" />)}</div><span className="sr-only">Loading campaign assets</span></div>;
}
