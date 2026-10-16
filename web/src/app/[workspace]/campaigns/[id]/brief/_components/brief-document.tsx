import { ExternalLink, Sparkles } from "lucide-react";
import type { ReactNode } from "react";

import { Badge } from "@/components/ui/badge";
import { Alert } from "@/components/ui/alert";
import { webUrl } from "@/components/opportunities/format";
import { day, fields, money, type BriefDetail, type SmartBrief } from "../_lib/model";

export function BriefWarnings({ warnings }: { warnings: BriefDetail["warnings"] }) {
  if (!warnings.length) return null;
  return <div className="space-y-2" aria-label="Brief checks">{warnings.map((warning, index) => <Alert key={`${warning.code}-${warning.field}-${index}`} variant={warning.severity === "blocking" ? "destructive" : warning.severity === "info" ? "info" : "warning"}><p>{warning.message}</p></Alert>)}</div>;
}

export function Reason({ brief, field }: { brief: SmartBrief; field: string }) {
  const reason = brief.reasons.find(item => item.field === field);
  return <div className="space-y-2 rounded-md bg-muted p-3 text-small text-muted-foreground">
    <p><span className="font-semibold text-foreground">Why · </span>{reason?.reason || (brief.origin === "edit" ? "Set by a person." : "No reasoning was supplied for this field.")}</p>
    {reason?.evidence.map((item, index) => {
      const url = webUrl(item.source);
      return <p key={index} className="wrap-anywhere"><span className="font-medium text-foreground">{item.label}: </span>{item.value}{url ? <> · <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-primary underline underline-offset-4">Source<ExternalLink className="size-3" aria-hidden="true" /></a></> : item.source ? <> · {item.source}</> : null}</p>;
    })}
    {reason && !reason.evidence.length && brief.origin !== "edit" && <p className="text-warning-strong">No supporting source supplied.</p>}
  </div>;
}

export function DraftLabel({ brief, approved = false }: { brief: SmartBrief; approved?: boolean }) {
  return <div className="flex flex-wrap items-center gap-2"><Badge variant="neutral">Version {brief.version}</Badge>{!approved && ["drafted", "instruction", "trigger", "starter"].includes(brief.origin) && <span className="inline-flex items-center gap-1.5 text-small text-muted-foreground"><Sparkles className="size-4" aria-hidden="true" />Drafted by Kairo</span>}</div>;
}

export function BriefDocument({ detail }: { detail: BriefDetail }) {
  const brief = detail.brief!;
  const label = (choices: { value: string; label: string }[], value: string) => choices.find(choice => choice.value === value)?.label ?? value;
  const values: Record<string, ReactNode> = {
    title: brief.title, objective: brief.objective, kpi: label(detail.options.kpis, brief.kpi),
    campaignTypes: brief.campaignTypes.map(value => label(detail.options.campaignTypes, value)).join(", "),
    budget: brief.budget ? money(brief.budget.amount, brief.budget.currency) : "Not set",
    dates: `${day(brief.startDate)} to ${day(brief.endDate)}`, regions: brief.regions.join(", "),
    audiences: brief.audiences.join("; "), keyMessages: brief.keyMessages.join("; "), products: brief.products.map(product => `${product.title}${product.inStock ? "" : " (out of stock)"}`).join(", "),
    landingPages: <ul className="space-y-1">{brief.landingPages.map((page, index) => <li key={index}>{webUrl(page) ? <a href={webUrl(page)} target="_blank" rel="noopener noreferrer" className="text-primary underline underline-offset-4">{page}</a> : page}</li>)}</ul>,
    languages: brief.languages.map(value => label(detail.options.languages, value)).join(", "),
  };
  return <dl className="space-y-6">{fields.map(([field, label]) => <div key={field} className="space-y-2 wrap-anywhere"><dt className="text-small font-semibold">{label}</dt><dd className="space-y-2"><div>{values[field] || "Not set"}</div><Reason brief={brief} field={field} /></dd></div>)}</dl>;
}
