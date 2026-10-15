import Link from "next/link";

import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { importOutcome, missingLabels } from "../_lib/catalogue";
import type { CatalogueImport } from "../_lib/types";

export function ImportResult({ result, handle, detail = false }: { result: CatalogueImport; handle: string; detail?: boolean }) {
  const outcome = importOutcome(result);
  return <div className="flex flex-col gap-3">
    <div className="flex flex-wrap items-center gap-3"><Badge variant={outcome.status === "saved" ? "success" : result.status === "suspicious" ? "warning" : "destructive"}>{result.status.charAt(0).toUpperCase() + result.status.slice(1)}</Badge><span className="text-small wrap-anywhere text-muted-foreground">{result.fileName ?? result.feedUrl ?? "Catalogue import"}</span></div>
    <Alert role={outcome.status === "saved" ? "status" : "alert"} variant={outcome.status === "saved" ? "success" : "warning"}>{outcome.message}</Alert>
    <dl className="grid grid-cols-2 gap-3 text-small sm:grid-cols-4">{[["Added", result.added], ["Updated", result.updated], ["Removed", result.removed], ["Invalid rows", result.invalid]].map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="font-semibold tabular-nums">{value ?? 0}</dd></div>)}</dl>
    {!detail && <Link href={`/${handle}/brand-kit/catalogue/imports/${result.id}`} className="self-start text-small font-medium text-primary hover:underline">Review import details</Link>}
    {detail && <div><h2 className="mb-3 text-base font-semibold">Import issues</h2>{!result.problems?.length ? <p className="text-small text-muted-foreground">No row issues reported.</p> : <ul className="divide-y divide-border">{result.problems.map((problem, i) => <li key={i} className="py-3 text-small wrap-anywhere"><p className="font-semibold">Row {problem.row} · {missingLabels[problem.attribute] ?? problem.attribute}</p><p className="text-muted-foreground">{problem.message}</p></li>)}</ul>}</div>}
  </div>;
}
