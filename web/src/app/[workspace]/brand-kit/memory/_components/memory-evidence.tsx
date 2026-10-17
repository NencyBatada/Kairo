function title(key: string) { return key.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[_-]/g, " ").replace(/^./, char => char.toUpperCase()); }

export function MemoryEvidence({ value, depth = 0 }: { value: unknown; depth?: number }) {
  if (value === null || value === undefined) return <p className="text-small text-muted-foreground">No supporting evidence supplied.</p>;
  if (typeof value !== "object") return <p className="text-small whitespace-pre-wrap wrap-anywhere">{typeof value === "boolean" ? value ? "Yes" : "No" : String(value)}</p>;
  const entries = Object.entries(value).filter(([, item]) => item !== null);
  if (!entries.length) return <p className="text-small text-muted-foreground">No supporting evidence supplied.</p>;
  const record = value as Record<string, unknown>;
  const numeric = (item: unknown) => (typeof item === "number" || typeof item === "string" && item.trim()) && Number.isFinite(Number(item)) ? Number(item) : null;
  const lift = numeric(record.lift), low = numeric(record.low), high = numeric(record.high);
  const hasLift = Object.hasOwn(record, "lift"), complete = lift !== null && low !== null && high !== null && low <= lift && lift <= high;
  const percent = (item: number) => `${item < 0 ? "−" : "+"}${Math.abs(item * 100).toFixed(1)}%`;
  return <dl className="space-y-3 text-small">{hasLift && <div><dt className="font-medium">Lift</dt><dd className="mt-1 text-muted-foreground">{complete ? `${percent(lift)} (90% range: ${percent(low)} to ${percent(high)})` : "A complete lift estimate and range were not supplied."}</dd></div>}{entries.filter(([key]) => !hasLift || !["lift", "low", "high"].includes(key)).map(([key, item]) => <div key={key} className="min-w-0"><dt className="font-medium wrap-anywhere">{Array.isArray(value) ? `Item ${Number(key) + 1}` : title(key)}</dt><dd className="mt-1 whitespace-pre-wrap wrap-anywhere text-muted-foreground">{typeof item === "object" && item !== null ? depth < 6 ? <details className="border-l border-border pl-3"><summary className="min-h-11 cursor-pointer py-3 text-primary">View details</summary><MemoryEvidence value={item} depth={depth + 1} /></details> : "Further nested details are not displayed." : typeof item === "boolean" ? item ? "Yes" : "No" : String(item)}</dd></div>)}</dl>;
}
