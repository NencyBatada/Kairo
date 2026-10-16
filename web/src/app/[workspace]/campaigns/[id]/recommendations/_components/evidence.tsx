import { label, money, number, numeric, object } from "../_lib/model";

export function Evidence({ value, currency, depth = 0 }: { value: unknown; currency: string; depth?: number }) {
  const entries = Array.isArray(value) ? value.map((item, i) => [String(i + 1), item] as const) : Object.entries(object(value)).filter(([, item]) => item !== null);
  if (!entries.length) return <p className="text-small text-muted-foreground">No supporting details supplied.</p>;
  return <dl className="space-y-3 text-small">{entries.map(([key, item]) => <div key={key} className="min-w-0"><dt className="font-medium wrap-anywhere">{label(key.replace(/Micros$/, ""))}</dt><dd className="mt-1 wrap-anywhere text-muted-foreground">{item !== null && typeof item === "object" ? depth < 8 ? <details className="border-l border-border pl-3"><summary className="min-h-11 cursor-pointer py-3 text-primary">View {label(key).toLowerCase()}</summary><Evidence value={item} currency={currency} depth={depth + 1} /></details> : "Open Google Ads to review deeper details." : /Micros$/.test(key) && numeric(item) !== null ? money(numeric(item)! / 1_000_000, currency) : typeof item === "boolean" ? item ? "Yes" : "No" : typeof item === "number" ? number(item) : String(item ?? "Not supplied")}</dd></div>)}</dl>;
}
