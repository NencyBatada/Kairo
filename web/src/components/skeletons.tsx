import { Skeleton } from "@/components/ui/skeleton";

export function SidebarSkeleton() {
  return (
    <div className="flex h-full flex-col gap-5 px-3 pt-4 pb-3" aria-hidden="true">
      <Skeleton className="h-7 w-24" />
      <Skeleton className="h-10 w-full rounded-lg" />
      <div className="flex flex-col gap-2">
        {Array.from({ length: 7 }, (_, index) => (
          <Skeleton key={index} className="h-8 w-full" />
        ))}
      </div>
    </div>
  );
}

export function PageSkeleton() {
  return (
    <div className="flex flex-col gap-8">
      <span role="status" className="sr-only">
        Loading
      </span>
      <div className="flex flex-col gap-3" aria-hidden="true">
        <Skeleton className="h-10 w-56" />
        <Skeleton className="h-5 w-full max-w-md" />
      </div>
      <Skeleton className="h-64 w-full rounded-lg" aria-hidden="true" />
    </div>
  );
}
