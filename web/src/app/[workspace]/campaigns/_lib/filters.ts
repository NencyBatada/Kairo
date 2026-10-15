export const stages: Record<string, string> = { brief_draft: "Brief draft", creating: "Creating", asset_review: "Asset review", ready_to_launch: "Ready to launch", launching: "Launching", live: "Live", results_ready: "Results ready", closed: "Closed", discarded: "Discarded" };
export const statuses: Record<string, string> = { in_progress: "In progress", agent_working: "Agent working", waiting_approval: "Waiting for approval", needs_attention: "Needs attention", closed: "Closed", discarded: "Discarded" };

export type CampaignFilters = Record<"q" | "stage" | "status" | "owner" | "from" | "to" | "before", string>;
export type FilterErrors = Partial<CampaignFilters>;

export function validDay(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && value >= "0001-01-01" && !Number.isNaN(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
}

export function campaignFilters(search: Record<string, string | string[] | undefined>) {
  const errors: FilterErrors = {};
  const values = Object.fromEntries(["q", "stage", "status", "owner", "from", "to", "before"].map((key) => {
    const value = search[key];
    if (Array.isArray(value)) errors[key as keyof CampaignFilters] = "Use one value for this filter.";
    return [key, typeof value === "string" ? value.trim() : ""];
  })) as CampaignFilters;
  if (values.q.length > 100) errors.q = "Search with 100 characters or fewer.";
  if (values.stage && !Object.hasOwn(stages, values.stage)) errors.stage = "Choose a campaign stage from the list.";
  if (values.status && !Object.hasOwn(statuses, values.status)) errors.status = "Choose a campaign status from the list.";
  if (values.owner && !/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(values.owner)) errors.owner = "Choose a campaign owner from the list.";
  if (values.from && !validDay(values.from)) errors.from = "Choose a valid opening date.";
  if (values.to && !validDay(values.to)) errors.to = "Choose a valid ending date.";
  if (validDay(values.from) && validDay(values.to) && values.to < values.from) errors.to = "Choose an end date on or after the start date.";
  if (values.before && (!/^[1-9]\d{0,18}$/.test(values.before) || BigInt(values.before) > BigInt("3155378975999999999"))) errors.before = "This page link is invalid. Return to the latest campaigns.";
  return { values, errors };
}

export function apiFilters(values: CampaignFilters) {
  return { Q: values.q || undefined, Stage: values.stage || undefined, Status: values.status || undefined, Owner: values.owner || undefined, From: values.from || undefined, To: values.to || undefined, Before: values.before || undefined };
}

export function filterQuery(values: CampaignFilters, before?: string) {
  return new URLSearchParams(Object.entries({ ...values, before: before ?? "" }).filter(([, value]) => !!value)).toString();
}
