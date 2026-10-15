export const statuses = ["open", "new", "saved", "accepted", "dismissed", "expired"] as const;
type Status = typeof statuses[number];

export function opportunityFilters(params: Record<string, string | string[] | undefined>) {
  const single = (key: string) => typeof params[key] === "string" ? params[key].trim() : "";
  const status = single("status");
  const minimum = single("minScore");
  return {
    status: (statuses.includes(status as Status) ? status : "open") as Status,
    category: single("category").slice(0, 100),
    watched: single("watched") === "true" ? true : undefined,
    minScore: /^\d{1,3}$/.test(minimum) && Number(minimum) <= 100 ? Number(minimum) : undefined,
  };
}
