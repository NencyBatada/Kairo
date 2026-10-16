import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { count, date, difference, type Lift, type Reading } from "../_lib/model";
import { dates } from "../../_lib/presentation";

function validRange(estimate: Reading["lift"], low: Reading["low"], high: Reading["high"]) {
  return [estimate, low, high].every(value => (typeof value === "number" || typeof value === "string" && value.trim() !== "") && Number.isFinite(Number(value))) && Number(low) <= Number(estimate) && Number(estimate) <= Number(high);
}

const reasons: Record<string, string> = {
  short_history: "There is not enough history before the test. More history is needed for a reliable comparison.",
  no_outcome: "There are too few outcomes in the test or holdout data to estimate lift.",
  few_placebos: "There are too few comparable historical windows to calculate a reliable range.",
  no_data: "Outcome data has not been loaded for this window yet. Check the analytics and sales feeds.",
  no_test_days: "No completed test days are available to measure yet.",
};

export function EarlyReading({ lift, timeZone }: { lift: Lift; timeZone: string }) {
  const reading = lift.latestEarly;
  if (!reading) return <section aria-labelledby="early-lift-title" className="space-y-3 rounded-xl border border-border bg-card p-4 sm:p-6"><h2 id="early-lift-title" className="text-base font-semibold">Early lift reading</h2><p className="text-small text-muted-foreground">No early lift reading yet. Readings start after at least seven test days are covered by loaded data and are checked hourly. They never trigger recommendations.</p></section>;
  const available = reading.early && reading.status === "ok" && validRange(reading.lift, reading.low, reading.high);
  const kpi = reading.kpi === "orders" ? "orders" : reading.kpi === "conversions" ? "conversions" : "results";
  const inconclusive = !reading.conclusive || Number(reading.low) <= 0 && Number(reading.high) >= 0;
  const point = Number(reading.high) === Number(reading.low) ? 50 : (Number(reading.lift) - Number(reading.low)) / (Number(reading.high) - Number(reading.low)) * 100;
  return <section aria-labelledby="early-lift-title" className="space-y-5 rounded-xl border border-border bg-card p-4 sm:p-6"><div className="flex flex-wrap items-center gap-3"><h2 id="early-lift-title" className="text-base font-semibold">Early lift reading</h2><Badge variant="info">Early reading</Badge>{available && inconclusive && <Badge variant="neutral">Inconclusive</Badge>}</div>
    <p className="text-small text-muted-foreground">{date(reading.startDate)} to {date(reading.throughDate)} · {count(reading.days)} of {count(reading.plannedDays)} planned days. Calculated {dates(timeZone).format(new Date(reading.createdAt))}.</p>
    <Alert variant="info">{lift.final ? "This is the last early reading, not the final result. " : "This estimate may change as more data arrives. "}Early readings never trigger budget or target recommendations.</Alert>
    {available ? <><div className="space-y-3"><h3 className="text-sm font-semibold">Estimated lift in {kpi}</h3><p className="font-display text-display-md wrap-anywhere tabular-nums">{difference(reading.lift!)} <span className="font-sans text-small text-muted-foreground">(90% range: {difference(reading.low!)} to {difference(reading.high!)})</span></p><div role="img" aria-label={`Early estimated lift ${difference(reading.lift!)}; 90% range ${difference(reading.low!)} to ${difference(reading.high!)}`} className="relative mx-1 h-3 rounded-full bg-info-soft"><span className="absolute inset-y-0 left-0 w-full rounded-full border border-info/40" /><span className="absolute top-1/2 size-3 -translate-x-1/2 -translate-y-1/2 rounded-full bg-info" style={{ left: `${point}%` }} /></div><p className="text-small text-muted-foreground">{inconclusive ? "The early reading does not establish an effect. Keep collecting data through the planned test window." : "The early range excludes zero, but this is still an interim estimate. Wait for the final report before deciding what worked."}</p></div>
      {validRange(reading.extra, reading.extraLow, reading.extraHigh) && <p className="text-small tabular-nums">Estimated extra {kpi}: {count(reading.extra!)} (90% range: {count(reading.extraLow!)} to {count(reading.extraHigh!)}).</p>}
      {reading.actual !== null && reading.expected !== null && <dl className="grid gap-4 text-small sm:grid-cols-2"><div><dt className="text-muted-foreground">Actual {kpi} in test regions</dt><dd className="tabular-nums">{count(reading.actual)}</dd></div><div><dt className="text-muted-foreground">Expected from holdout comparison</dt><dd className="tabular-nums">{count(reading.expected)}</dd></div></dl>}
    </> : <Alert variant="neutral"><strong>Not enough evidence for a lift estimate</strong><p>{reading.reason ? reasons[reading.reason] ?? "The service could not produce a reliable estimate for this window. Review data coverage and the proof plan." : "A complete estimate and range are not available. Keep collecting data before drawing a conclusion."}</p></Alert>}
  </section>;
}
