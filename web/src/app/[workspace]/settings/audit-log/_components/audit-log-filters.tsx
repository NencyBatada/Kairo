import Link from "next/link";

import { buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import type { AuditActionOption } from "@/lib/core-api/types";
import { cn } from "@/lib/utils";

import { actorTypeLabels, auditLogQuery, hasFilters, type AuditLogSearch } from "./audit-log-search";

type AuditLogFiltersProps = {
  workspace: string;
  brandId: string;
  actions: AuditActionOption[];
  search: AuditLogSearch;
};

const fieldClass = "flex min-w-0 flex-col gap-1.5";

export function AuditLogFilters({ workspace, brandId, actions, search }: AuditLogFiltersProps) {
  const base = `/${workspace}/settings/audit-log`;
  const exportHref = `/api/brands/${encodeURIComponent(brandId)}/audit-log/export${auditLogQuery(search)}`;
  const known = new Set(actions.map((option) => option.action));
  const unknown = search.actions.filter((action) => !known.has(action));

  return (
    <form method="get" action={base} className="flex flex-col gap-4">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
        <div className={fieldClass}>
          <Label htmlFor="audit-action">What</Label>
          <NativeSelect id="audit-action" name="action" defaultValue={search.actions[0] ?? ""}>
            <option value="">All actions</option>
            {actions.map((option) => (
              <option key={option.action} value={option.action}>
                {option.label}
              </option>
            ))}
            {unknown.map((action) => (
              <option key={action} value={action}>
                {action}
              </option>
            ))}
          </NativeSelect>
        </div>
        <div className={fieldClass}>
          <Label htmlFor="audit-actor">Who</Label>
          <Input id="audit-actor" name="actor" defaultValue={search.actor ?? ""} placeholder="Name or email" maxLength={200} />
        </div>
        <div className={fieldClass}>
          <Label htmlFor="audit-actor-type">Done by</Label>
          <NativeSelect id="audit-actor-type" name="actorType" defaultValue={search.actorType ?? ""}>
            <option value="">Anyone</option>
            {Object.entries(actorTypeLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </NativeSelect>
        </div>
        <div className={fieldClass}>
          <Label htmlFor="audit-from">From</Label>
          <Input id="audit-from" name="from" type="date" defaultValue={search.from ?? ""} />
        </div>
        <div className={fieldClass}>
          <Label htmlFor="audit-to">To</Label>
          <Input id="audit-to" name="to" type="date" defaultValue={search.to ?? ""} />
        </div>
      </div>
      <div className="flex flex-wrap gap-2">
        <button type="submit" className={cn(buttonVariants({ size: "sm" }), "max-sm:w-full pointer-coarse:min-h-11")}>
          Apply filters
        </button>
        {hasFilters(search) && (
          <Link href={base} className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "pointer-coarse:min-h-11")}>
            Clear filters
          </Link>
        )}
        <a href={exportHref} download className={cn(buttonVariants({ variant: "secondary", size: "sm" }), "pointer-coarse:min-h-11")}>
          Export CSV
        </a>
      </div>
    </form>
  );
}
