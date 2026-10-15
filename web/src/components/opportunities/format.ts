export type Numeric = number | string;

export function number(value: Numeric) {
  return new Intl.NumberFormat("en-IN", { maximumFractionDigits: 0 }).format(Number(value));
}

export function growth(value: Numeric | null) {
  if (value === null) return "Not available";
  const amount = Number(value) * 100;
  return `${amount < 0 ? "−" : "+"}${Math.abs(amount).toFixed(1)}%`;
}

export function webUrl(value: string | null) {
  if (!value) return undefined;
  try {
    const url = new URL(value);
    return ["https:", "http:"].includes(url.protocol) ? url.href : undefined;
  } catch {
    return undefined;
  }
}

export function month(value: string) {
  return new Intl.DateTimeFormat("en-IN", { month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(`${value}-01T00:00:00Z`));
}
