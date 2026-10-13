export const DEFAULT_TIME_ZONE = "Asia/Kolkata";

const CURRENT_NAMES: Record<string, string> = {
  "Asia/Calcutta": "Asia/Kolkata",
  "Asia/Katmandu": "Asia/Kathmandu",
  "Asia/Rangoon": "Asia/Yangon",
  "Asia/Saigon": "Asia/Ho_Chi_Minh",
  "Europe/Kiev": "Europe/Kyiv",
};

export function timeZoneOptions(zones: readonly string[] = Intl.supportedValuesOf("timeZone")): string[] {
  return [...new Set([...zones.map((zone) => CURRENT_NAMES[zone] ?? zone), DEFAULT_TIME_ZONE])].sort();
}
