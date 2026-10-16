import { webUrl } from "@/components/opportunities/format";

export function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function items(value: unknown) {
  return Array.isArray(value) ? value.map(record) : [];
}

export function evidenceSnapshot(value: unknown) {
  const data = record(value);
  return {
    topic: typeof data.topic === "string" ? data.topic : null,
    regions: items(data.regions).flatMap((item) => typeof item.name === "string" ? [item.name] : []),
    products: items(data.products).flatMap((item) => typeof item.title === "string" ? [item.title] : []),
    reasons: items(data.reasons).flatMap((reason) => typeof reason.text === "string" ? [{ text: reason.text, sources: items(reason.sources).flatMap((source) => typeof source.title === "string" && typeof source.url === "string" && webUrl(source.url) ? [{ title: source.title, url: webUrl(source.url)! }] : []) }] : []),
    trigger: typeof record(data.trigger).summary === "string" ? record(data.trigger).summary as string : null,
  };
}

export function activityFacts(value: unknown) {
  const data = record(value);
  const labels: Record<string, string> = { comment: "Comment", reason: "Reason", from: "From", to: "To", state: "Stage", stage: "Stage", gate: "Approval gate", status: "Status", title: "Campaign", error: "Issue", message: "Message" };
  return Object.entries({ ...data, ...record(data.details) }).flatMap(([key, item]) => {
    const label = labels[key.toLowerCase()];
    return label && typeof item === "string" && item ? [{ label, value: ["from", "to", "state", "stage", "gate", "status"].includes(key.toLowerCase()) ? item.replaceAll("_", " ") : item }] : [];
  });
}
