import Link from "next/link";

import { auditActionOptions } from "./audit-actions";
import { buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { cn } from "@/lib/utils";

type AuditLogFiltersProps = {
  workspace: string;
  action?: string;
  actor?: string;
  before?: string;
};

export function AuditLogFilters({ workspace, action, actor, before }: AuditLogFiltersProps) {
  const base = `/${workspace}/settings/audit-log`;
  const exportParams = new URLSearchParams();
  if (action) exportParams.set("action", action);
  if (actor) exportParams.set("actor", actor);
  const exportHref = `${base}/export${exportParams.size ? `?${exportParams}` : ""}`;

  return (
    <form method="get" className="flex flex-col gap-4 md:flex-row md:flex-wrap md:items-end">
      {before && <input type="hidden" name="before" value={before} />}
      <div className="flex min-w-0 flex-1 flex-col gap-1.5 md:max-w-xs">
        <Label htmlFor="audit-action">What</Label>
        <NativeSelect id="audit-action" name="action" defaultValue={action ?? ""}>
          <option value="">All actions</option>
          {auditActionOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </NativeSelect>
      </div>
      <div className="flex min-w-0 flex-1 flex-col gap-1.5 md:max-w-xs">
        <Label htmlFor="audit-actor">Who</Label>
        <Input id="audit-actor" name="actor" defaultValue={actor ?? ""} placeholder="Search by name" />
      </div>
      <div className="flex flex-wrap gap-2">
        <button type="submit" className={cn(buttonVariants({ size: "sm" }), "pointer-coarse:min-h-11")}>
          Apply filters
        </button>
        {(action || actor) && (
          <Link href={before ? `${base}?before=${encodeURIComponent(before)}` : base} className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "pointer-coarse:min-h-11")}>
            Clear filters
          </Link>
        )}
        <Link href={exportHref} className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "pointer-coarse:min-h-11")}>
          Export CSV
        </Link>
      </div>
    </form>
  );
}
