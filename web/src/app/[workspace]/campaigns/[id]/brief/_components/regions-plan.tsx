"use client";

import { useState } from "react";
import type { ProofResult } from "../_lib/model";
import { percent } from "../_lib/model";
import type { MapRegion } from "../_lib/map";
import { ProofSummary } from "./proof-summary";
import { RegionControls } from "./region-controls";
import { RegionsMap } from "./regions-map";

export function RegionsPlan({ result, brandId, campaignId, campaignVersion, canEdit }: { result: ProofResult; brandId: string; campaignId: string; campaignVersion: string; canEdit: boolean }) {
  const [revision, setRevision] = useState(0);
  const plan = result.plan;
  if (!plan) return <ProofSummary result={result} />;
  const regions: MapRegion[] = [...plan.testRegions.map(region => ({ ...region, kind: "test" as const })), ...plan.holdouts.map(region => ({ ...region, kind: "holdout" as const }))].map((region, index) => ({ ...region, number: index + 1 }));
  return <ProofSummary result={result}>
    <div className="space-y-3 border-t pt-5"><h3 className="text-lg font-semibold">Test and holdout regions</h3><p className="text-small text-muted-foreground">The plan compares {plan.unit === "state" ? "states" : "cities"} using {plan.kpi === "orders" ? "orders" : plan.kpi === "conversions" ? "conversions" : plan.kpi}. {plan.chosenByUser ? "Holdouts were chosen by a marketer." : "Kairo selected the holdouts from historical matches."}</p><RegionsMap regions={regions} /></div>
    {plan.liftByWindow.length > 0 && <details className="rounded-md border p-4"><summary className="min-h-6 cursor-pointer text-small font-semibold">Detectable lift by test length</summary><dl className="mt-3 divide-y">{plan.liftByWindow.map((window, index) => <div key={index} className="flex flex-wrap justify-between gap-3 py-2 text-small"><dt>{window.days} days</dt><dd className="tabular-nums">{percent(window.lift)}</dd></div>)}</dl></details>}
    {canEdit ? <RegionControls key={revision} result={result} brandId={brandId} campaignId={campaignId} campaignVersion={campaignVersion} onReload={() => setRevision(value => value + 1)} /> : <p className="text-small text-muted-foreground">A marketer can choose holdouts while the draft is editable. Changes after launch require a recorded reason.</p>}
  </ProofSummary>;
}
