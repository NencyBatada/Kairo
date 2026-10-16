import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { day, money, percent, type ProofResult } from "../_lib/model";

export function ProofSummary({ result }: { result: ProofResult }) {
  const plan = result.plan;
  return <section id="proof-plan" aria-labelledby="proof-title" className="space-y-5 rounded-lg border bg-card p-5 sm:p-6">
    <div className="space-y-1"><p className="text-small text-muted-foreground">Proof by design</p><h2 id="proof-title" className="text-xl font-semibold">Test plan</h2></div>
    {result.updating && <Alert variant="info" role="status"><p>Kairo is recalculating the test plan. Any numbers below are from the last saved plan.</p></Alert>}
    {!plan ? <p className="text-muted-foreground">The test plan will appear when the Plan agent has finished designing it.</p> : <>
      <div className="flex flex-wrap gap-2"><Badge variant="neutral">Version {plan.version}</Badge>{plan.lockedAt && <Badge variant="gate">Locked at launch</Badge>}{plan.changedAfterLock && <Badge variant="warning">Changed after launch</Badge>}{plan.approvedWithWarnings && <Badge variant="warning">Approved with data warnings</Badge>}</div>
      <dl className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">{[
        ["Test window", `${day(plan.window.start)} to ${day(plan.window.end)}${plan.window.days ? ` · ${plan.window.days} days` : ""}`],
        ["Smallest detectable lift", percent(plan.minDetectableLift)], ["Expected holdout cost", plan.holdoutCost ? money(plan.holdoutCost.amount, plan.holdoutCost.currency) : "Not available"],
        ["Holdout share", percent(plan.holdoutShare)], ["History available", `${plan.dataDays} days`], ["Historical test windows", String(plan.placeboWindows)],
      ].map(([label, value]) => <div key={label} className="space-y-1 wrap-anywhere"><dt className="text-small text-muted-foreground">{label}</dt><dd className="font-medium tabular-nums">{value}</dd></div>)}</dl>
      <p className="text-small text-muted-foreground">Smallest detectable lift estimates how large a change this test can reliably detect. It is not a forecast of campaign results.</p>
      <div className="grid gap-4 sm:grid-cols-2"><div><h3 className="font-semibold">Test regions</h3><p className="mt-1 wrap-anywhere text-small">{plan.testRegions.map(region => region.name).join(", ") || "No test regions"}</p></div><div><h3 className="font-semibold">Holdout regions</h3><p className="mt-1 wrap-anywhere text-small">{plan.holdouts.map(region => region.name).join(", ") || "No holdout regions"}</p></div></div>
      {plan.changeReason && <Alert variant="warning"><p>Reason for the change after launch: {plan.changeReason}</p></Alert>}
      {plan.warnings.map((warning, index) => <Alert key={`${warning.code}-${index}`} variant="warning"><p>{warning.message}</p></Alert>)}
    </>}
  </section>;
}
