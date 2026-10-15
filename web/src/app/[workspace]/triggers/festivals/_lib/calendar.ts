import type { components } from "@/lib/core-api/schema";
import { text } from "@/lib/forms";

export type Festival = components["schemas"]["FestivalView"];
export function validDate(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && value >= "0001-01-01" && value <= "9999-12-31" && Number.isFinite(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
}
export function calendarDate(value: string) {
  return new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
}
export function readFestival(form: FormData) {
  const errors: Record<string, string[]> = {};
  const name = text(form, "name").split(/\s+/).join(" ");
  const startsOn = text(form, "startsOn");
  const endsOn = text(form, "endsOn") || startsOn;
  if (name.length < 2 || name.length > 80) errors.name = ["Use 2 to 80 characters."];
  if (!validDate(startsOn)) errors.startsOn = ["Choose a valid start date."];
  if (!validDate(endsOn)) errors.endsOn = ["Choose a valid end date."];
  if (!errors.startsOn && !errors.endsOn) {
    const days = (Date.parse(`${endsOn}T00:00:00Z`) - Date.parse(`${startsOn}T00:00:00Z`)) / 86400000 + 1;
    if (days < 1) errors.endsOn = ["Use a date on or after the start."];
    else if (days > 14) errors.endsOn = ["An event can last up to 14 days."];
  }
  const regions = [...new Set(form.getAll("regions").filter((value): value is string => typeof value === "string"))];
  return { request: { name, startsOn, endsOn, regions }, errors };
}
