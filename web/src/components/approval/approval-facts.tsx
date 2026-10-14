import { TriangleAlert } from "lucide-react";
import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

export type ApprovalFact = {
  label: string;
  value: ReactNode;
  note?: string;
  tone?: "default" | "warning";
};

export function ApprovalFacts({ facts, className }: { facts: ApprovalFact[]; className?: string }) {
  if (facts.length === 0) {
    return null;
  }

  return (
    <dl className={cn("flex flex-col divide-y divide-border rounded-lg border border-border", className)}>
      {facts.map((fact) => {
        const warning = fact.tone === "warning";

        return (
          <div
            key={fact.label}
            className="flex min-w-0 flex-col gap-0.5 px-3 py-2.5 md:grid md:grid-cols-[minmax(0,12rem)_minmax(0,1fr)] md:gap-4"
          >
            <dt className="text-small text-subtle-foreground">{fact.label}</dt>
            <dd className="flex min-w-0 flex-col gap-0.5">
              <span
                className={cn(
                  "flex min-w-0 items-start gap-1.5 text-sm font-medium tabular-nums wrap-anywhere",
                  warning ? "text-warning-strong" : "text-foreground",
                )}
              >
                {warning && <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden="true" />}
                <span className="min-w-0">{fact.value}</span>
              </span>
              {fact.note && (
                <span className={cn("text-small wrap-anywhere", warning ? "text-warning-strong" : "text-muted-foreground")}>{fact.note}</span>
              )}
            </dd>
          </div>
        );
      })}
    </dl>
  );
}
