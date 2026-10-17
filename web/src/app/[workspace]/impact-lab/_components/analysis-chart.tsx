"use client";

import { useId, useState } from "react";
import { amount, dailyRows, date, numeric, type Day } from "../_lib/model";
import { control } from "./setup-form";

export function AnalysisChart({ days, start, currency, metric }: { days: Day[]; start: string; currency: string | null; metric: string }) {
  const id = useId(), rows = dailyRows(days), [selected, setSelected] = useState("");
  if (!rows.length) return <p className="text-small text-muted-foreground">Daily actual and expected data are not available.</p>;
  const active = rows.find(day => day.date === selected) ?? rows.at(-1)!;
  const dates = rows.map(day => Date.parse(day.date)), first = dates[0], last = dates.at(-1)!;
  const values = rows.flatMap(day => [numeric(day.actual), numeric(day.expected)]).filter((value): value is number => value !== null);
  const min = Math.min(0, ...values), max = Math.max(1, ...values), span = max - min;
  const x = (value: string) => first === last ? 300 : 24 + (Date.parse(value) - first) / (last - first) * 552;
  const y = (value: number) => 180 - (value - min) / span * 152;
  const path = (key: "actual" | "expected") => {
    let connected = false;
    return rows.map(day => { const value = numeric(day[key]); if (value === null) { connected = false; return ""; } const point = `${connected ? "L" : "M"}${x(day.date)},${y(value)}`; connected = true; return point; }).join(" ");
  };
  const startX = x(start), showStart = Date.parse(start) >= first && Date.parse(start) <= last;
  return <figure className="min-w-0 space-y-4" aria-labelledby={`${id}-title`}>
    <figcaption id={`${id}-title`} className="font-semibold">Actual versus expected · {metric}{currency ? ` (${currency})` : ""}</figcaption>
    <p className="max-w-prose text-small text-muted-foreground">Expected is the model’s estimate of what would have happened without the change. The chart shows daily values; the effect and its range describe the change period.</p>
    <div className="flex flex-wrap gap-x-5 gap-y-2 text-small"><span className="flex items-center gap-2"><span className="w-6 border-t-2 border-chart-1" />Actual</span><span className="flex items-center gap-2"><span className="w-6 border-t-2 border-dashed border-muted-foreground" />Expected without the change</span></div>
    <div className="grid grid-cols-[3.5rem_minmax(0,1fr)] gap-2"><div aria-hidden="true" className="flex h-56 flex-col justify-between py-6 text-right text-xs tabular-nums text-muted-foreground">{[max, (min + max) / 2, min].map((value, index) => <span key={index} className="wrap-anywhere">{new Intl.NumberFormat("en-IN", { notation: "compact", maximumFractionDigits: 1 }).format(value)}</span>)}</div><div className="min-w-0"><svg viewBox="0 0 600 212" preserveAspectRatio="none" className="block h-56 w-full" role="img" aria-label="Daily actual and expected outcomes. Use Inspect day or the data table for exact values.">
      {[max, (min + max) / 2, min].map((value, index) => <line key={index} x1="24" x2="576" y1={y(value)} y2={y(value)} stroke="var(--border)" vectorEffect="non-scaling-stroke" />)}
      {showStart && <line x1={startX} x2={startX} y1="20" y2="184" stroke="var(--foreground)" strokeDasharray="2 4" vectorEffect="non-scaling-stroke" />}
      <path d={path("expected")} fill="none" stroke="var(--muted-foreground)" strokeWidth="2" strokeDasharray="6 4" vectorEffect="non-scaling-stroke" />
      <path d={path("actual")} fill="none" stroke="var(--chart-1)" strokeWidth="2" vectorEffect="non-scaling-stroke" />
      {(["expected", "actual"] as const).map(key => numeric(active[key]) !== null && <circle key={key} cx={x(active.date)} cy={y(numeric(active[key])!)} r="4" fill={key === "actual" ? "var(--chart-1)" : "var(--muted-foreground)"} />)}
    </svg><div aria-hidden="true" className="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground"><span>{date(rows[0].date)}</span><span>{date(rows.at(-1)!.date)}</span></div></div></div>
    <p className="text-small text-muted-foreground">Change started {date(start)}{showStart ? " · dotted vertical marker" : " · outside the returned daily window"}.</p>
    <div className="max-w-80 space-y-1.5"><label className="text-small font-medium" htmlFor={`${id}-day`}>Inspect day</label><select className={control} id={`${id}-day`} value={active.date} onChange={event => setSelected(event.target.value)}>{rows.map(day => <option key={day.date} value={day.date}>{date(day.date)}</option>)}</select></div>
    <p role="status" className="rounded-md bg-muted p-3 text-small tabular-nums">{date(active.date)} · Actual {amount(active.actual, currency)} · Expected {amount(active.expected, currency)} · {active.during ? "During change" : "Before change"}</p>
    <details><summary className="min-h-11 cursor-pointer py-3 text-small font-medium text-primary">View daily data table</summary><div className="overflow-x-auto rounded-md border border-border" tabIndex={0} role="region" aria-label="Daily actual and expected data"><table className="w-full text-left text-small tabular-nums"><caption className="p-3 text-left">{metric}{currency ? ` (${currency})` : ""} · daily data returned by the analysis</caption><thead className="bg-muted"><tr>{["Date", "Actual", "Expected", "Period"].map(title => <th key={title} className="px-3 py-3 font-medium" scope="col">{title}</th>)}</tr></thead><tbody>{rows.map(day => <tr key={day.date} className="border-t border-border"><th scope="row" className="px-3 py-3 font-medium whitespace-nowrap">{date(day.date)}</th><td className="px-3 py-3">{amount(day.actual, currency)}</td><td className="px-3 py-3">{amount(day.expected, currency)}</td><td className="px-3 py-3">{day.during ? "During change" : "Before change"}</td></tr>)}</tbody></table></div></details>
  </figure>;
}
