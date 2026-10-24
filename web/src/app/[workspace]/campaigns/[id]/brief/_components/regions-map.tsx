"use client";

import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { bounds, coordinates, type MapRegion } from "../_lib/map";
import land from "../_lib/land.json";

export function RegionsMap({ regions }: { regions: MapRegion[] }) {
  const id = useId();
  const [selected, setSelected] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const svgRef = useRef<SVGSVGElement>(null);
  const [viewportWidth, setViewportWidth] = useState(640);
  const hasCoordinates = regions.some(region => coordinates(region));
  useEffect(() => {
    if (!svgRef.current || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(entries => { if (entries[0]?.contentRect.width) setViewportWidth(entries[0].contentRect.width); });
    observer.observe(svgRef.current);
    return () => observer.disconnect();
  }, [hasCoordinates]);
  const region = regions.find(region => region.id === selected);
  const visible = regions.filter(region => coordinates(region));
  const missing = regions.filter(region => !coordinates(region));
  const fitted = bounds(region ? [region] : visible);
  const width = fitted.width / zoom, height = fitted.height / zoom;
  const center = region ? coordinates(region) : null;
  const x = Math.max(0, Math.min(360 - width, (center?.x ?? fitted.x + fitted.width / 2) - width / 2));
  const y = Math.max(0, Math.min(180 - height, (center?.y ?? fitted.y + fitted.height / 2) - height / 2));
  const radius = width / viewportWidth * 6;
  return <div className="space-y-3">
    <div className="flex flex-wrap items-center justify-between gap-3"><div className="flex flex-wrap gap-4 text-small"><span className="flex items-center gap-2"><span className="size-3 rounded-full bg-chart-1" aria-hidden="true" />Test regions</span><span className="flex items-center gap-2"><span className="size-3 rotate-45 bg-chart-2" aria-hidden="true" />Holdout regions</span></div><div className="flex flex-wrap gap-2"><Button variant="secondary" type="button" disabled={!visible.length || zoom >= 4} onClick={() => setZoom(value => value * 2)} aria-label="Zoom in on regions map">Zoom in</Button><Button variant="secondary" type="button" disabled={zoom <= 1} onClick={() => setZoom(value => value / 2)} aria-label="Zoom out of regions map">Zoom out</Button><Button variant="ghost" type="button" onClick={() => { setSelected(null); setZoom(1); }}>Show all regions</Button></div></div>
    {visible.length ? <svg ref={svgRef} role="img" aria-labelledby={`${id}-title ${id}-description`} viewBox={`${x} ${y} ${width} ${height}`} className="aspect-[17/10] w-full rounded-lg border bg-info-soft" preserveAspectRatio="xMidYMid meet">
      <title id={`${id}-title`}>Saved test and holdout regions</title><desc id={`${id}-description`}>Circles are test regions; diamonds are holdout regions. Numbered locations and their names are listed below. This map shows region centres, not targeting boundaries.</desc>
      <path d={land.path} className="fill-muted stroke-border-strong" strokeWidth={width / 1800} fillRule="evenodd" />
      {visible.map(item => {
        const point = coordinates(item)!;
        return <g key={`${item.kind}-${item.id}`} className={item.kind === "test" ? "text-chart-1" : "text-chart-2"}><title>{`${item.number}. ${item.name} · ${item.kind === "test" ? "Test" : "Holdout"}`}</title>{selected === item.id && <circle cx={point.x} cy={point.y} r={radius * 1.7} fill="none" className="stroke-foreground" strokeWidth={radius / 3} />}{item.kind === "test" ? <circle cx={point.x} cy={point.y} r={radius} fill="currentColor" className="stroke-card" strokeWidth={radius / 4} /> : <path d={`M${point.x},${point.y - radius * 1.3}l${radius * 1.3},${radius * 1.3}l${-radius * 1.3},${radius * 1.3}l${-radius * 1.3},${-radius * 1.3}Z`} fill="currentColor" className="stroke-card" strokeWidth={radius / 4} />}<text x={point.x + radius * 1.5} y={point.y - radius} fontSize={radius * 2} className="fill-foreground stroke-card font-semibold" strokeWidth={radius / 4} paintOrder="stroke">{item.number}</text></g>;
      })}
    </svg> : <p className="rounded-md bg-muted p-4 text-small text-muted-foreground">No map coordinates are available for this plan. All regions are listed below.</p>}
    <p className="text-small text-muted-foreground">Region centres, not targeting boundaries. <a className="underline underline-offset-4" href="https://www.naturalearthdata.com/about/terms-of-use/" target="_blank" rel="noopener noreferrer">Land outline: Natural Earth</a>.</p>
    {missing.length > 0 && <p className="text-small text-warning-strong wrap-anywhere">Not plotted because coordinates are unavailable: {missing.map(region => region.name).join(", ")}.</p>}
    <ol className="grid gap-2 sm:grid-cols-2" aria-label="Saved regions">{regions.map(item => <li key={`${item.kind}-${item.id}`}><Button type="button" variant="ghost" disabled={!coordinates(item)} aria-pressed={selected === item.id} className="h-auto min-h-11 w-full justify-start whitespace-normal text-left" onClick={() => { setSelected(item.id); setZoom(1); }}><span className={item.kind === "test" ? "mt-1 size-2 shrink-0 rounded-full bg-chart-1" : "mt-1 size-2 shrink-0 rotate-45 bg-chart-2"} aria-hidden="true" /><span className="min-w-0 wrap-anywhere"><span className="font-medium text-foreground">{item.number}. {item.name}</span><span className="block text-small text-muted-foreground">{item.kind === "test" ? "Test" : "Holdout"}{item.state ? ` · ${item.state}` : ""}{!coordinates(item) ? " · no coordinates" : ""}</span></span></Button></li>)}</ol>
  </div>;
}
