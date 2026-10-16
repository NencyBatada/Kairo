"use client";

import { useId, useState } from "react";
import { chartPaths, date, money, type Day } from "../_lib/model";

export function SpendChart({ days, currency, name }: { days: Day[]; currency: string; name: string }) {
  const id = useId();
  const [selected, select] = useState<string | null>(null);
  const ordered = [...days].sort((a, b) => a.date.localeCompare(b.date));
  if (!ordered.length) return null;
  const first = Date.parse(ordered[0].date), last = Date.parse(ordered.at(-1)!.date);
  const max = Math.max(1, ...ordered.flatMap(day => [Number(day.spend), Number(day.plannedSpend)])) * 1.15;
  const x = (day: Day) => first === last ? 300 : 16 + (Date.parse(day.date) - first) / (last - first) * 568;
  const y = (value: number) => 170 - value / max * 140;
  const active = ordered.find(day => day.date === selected) ?? ordered.at(-1)!;
  return <figure className="min-w-0 space-y-3" aria-labelledby={`${id}-title`}>
    <figcaption id={`${id}-title`} className="text-small font-medium">{name} · daily spend against plan</figcaption>
    <div className="flex flex-wrap gap-x-5 gap-y-2 text-small text-muted-foreground"><span className="flex items-center gap-2"><span className="w-6 border-t-2 border-chart-1" />Actual spend</span><span className="flex items-center gap-2"><span className="w-6 border-t-2 border-dashed border-chart-2" />Planned spend</span></div>
    <div className="grid grid-cols-[4rem_minmax(0,1fr)] gap-2">
    <div aria-hidden="true" className="flex h-48 flex-col justify-between pt-6 pb-6 text-right text-xs tabular-nums text-muted-foreground">{[1, 0.5, 0].map(share => <span key={share}>{new Intl.NumberFormat("en-IN", { style: "currency", currency, notation: "compact", maximumFractionDigits: 1 }).format(max * share)}</span>)}</div>
    <div className="min-w-0"><svg viewBox="0 0 600 200" preserveAspectRatio="none" className="block h-48 w-full overflow-visible" role="group" aria-label={`${name} spend chart. Focus a point for its values; the daily data table follows.`}>
      {[0, 0.5, 1].map(share => <line key={share} x1="16" x2="584" y1={y(max * share)} y2={y(max * share)} stroke="var(--border)" vectorEffect="non-scaling-stroke" />)}
      <path d={chartPaths(ordered, "plannedSpend", x, y)} fill="none" stroke="var(--chart-2)" strokeWidth="2" strokeDasharray="6 4" vectorEffect="non-scaling-stroke" />
      <path d={chartPaths(ordered, "spend", x, y)} fill="none" stroke="var(--chart-1)" strokeWidth="2" vectorEffect="non-scaling-stroke" />
      {ordered.map(day => <circle key={day.date} cx={x(day)} cy={y(Number(day.spend))} r="5" tabIndex={0} role="img" className="outline-offset-4" fill="var(--chart-1)" stroke="var(--card)" strokeWidth="2" aria-label={`${date(day.date)}: actual ${money(day.spend, currency)}, planned ${money(day.plannedSpend, currency)}`} onMouseEnter={() => select(day.date)} onFocus={() => select(day.date)}><title>{date(day.date)}: {money(day.spend, currency)}</title></circle>)}
    </svg><div aria-hidden="true" className="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground"><span>{date(ordered[0].date)}</span>{first !== last && <span>{date(ordered.at(-1)!.date)}</span>}</div></div></div>
    <label htmlFor={`${id}-day`} className="block text-small font-medium">Inspect reporting day</label><select id={`${id}-day`} value={active.date} onChange={event => select(event.target.value)} className="min-h-11 w-full rounded-md border border-input bg-card px-3 text-base sm:w-64">{ordered.map(day => <option key={day.date} value={day.date}>{date(day.date)}</option>)}</select>
    <p className="rounded-md bg-muted px-3 py-2 text-small tabular-nums">{date(active.date)} · Actual {money(active.spend, currency)} · Plan {money(active.plannedSpend, currency)}</p>
  </figure>;
}
