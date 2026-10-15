"use client";

import { useId, useState } from "react";

import type { components } from "@/lib/core-api/schema";

import { month, number } from "./format";

type Point = components["schemas"]["TrendPointView"];

export function OpportunityTrend({ points, compact = false, label = "Monthly search demand" }: { points: Point[]; compact?: boolean; label?: string }) {
  const id = useId();
  const [active, setActive] = useState<number | null>(null);
  if (!points.length) return <p className="text-small text-muted-foreground">Search trend is not available yet.</p>;
  const height = compact ? 100 : 220;
  const maximum = Math.max(1, ...points.map((point) => Number(point.searches)));
  const x = (index: number) => 32 + (index / Math.max(1, points.length - 1)) * 536;
  const y = (point: Point) => height - 16 - (Number(point.searches) / maximum) * (height - 32);
  const path = points.map((point, index) => `${index ? "L" : "M"} ${x(index)} ${y(point)}`).join(" ");
  const selected = active === null ? null : points[active];
  return (
    <figure aria-labelledby={id} className="min-w-0">
      <figcaption id={id} className="text-small font-medium">{label}</figcaption>
      <div className="relative mt-3">
        <svg viewBox={`0 0 600 ${height}`} className="block w-full overflow-visible" role="img" aria-label={`${label}, ${month(points[0].month)} to ${month(points.at(-1)!.month)}`}>
          <line x1="32" x2="568" y1={height - 16} y2={height - 16} className="stroke-border-strong" />
          <line x1="32" x2="568" y1="16" y2="16" className="stroke-border" strokeDasharray="4 4" />
          <path d={path} fill="none" className="stroke-chart-1" strokeWidth="2.5" vectorEffect="non-scaling-stroke" />
          {points.map((point, index) => <circle key={point.month} cx={x(index)} cy={y(point)} r={active === index ? 5 : 4} className="fill-chart-1" onMouseEnter={() => setActive(index)} onMouseLeave={() => setActive(null)}>
            <title>{`${month(point.month)}: ${number(point.searches)} searches`}</title>
          </circle>)}
        </svg>
        <p className="min-h-5 text-small text-muted-foreground tabular-nums" aria-live="polite">{selected ? `${month(selected.month)} · ${number(selected.searches)} searches` : `Peak: ${number(maximum)} searches per month`}</p>
      </div>
      <div className="mt-1 flex justify-between gap-3 text-xs text-subtle-foreground"><span>{month(points[0].month)}</span><span>{month(points.at(-1)!.month)}</span></div>
      <details className="mt-3 text-small">
        <summary className="min-h-6 cursor-pointer text-primary pointer-coarse:min-h-11">View search data</summary>
        <table className="mt-2 w-full text-small tabular-nums">
          <caption className="sr-only">{label}</caption>
          <thead className="bg-muted text-subtle-foreground"><tr><th scope="col" className="p-2 text-left">Month</th><th scope="col" className="p-2 text-right">Searches</th></tr></thead>
          <tbody>{points.map((point) => <tr key={point.month} className="border-b border-border"><th scope="row" className="p-2 text-left font-normal">{month(point.month)}</th><td className="p-2 text-right">{number(point.searches)}</td></tr>)}</tbody>
        </table>
      </details>
    </figure>
  );
}
