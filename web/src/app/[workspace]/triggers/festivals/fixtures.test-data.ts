import type { components } from "@/lib/core-api/schema";
export const regions: components["schemas"]["PlaceView"][] = [{ id: "IN-MH", name: "Maharashtra", kind: "state", stateId: null }, { id: "IN-KA", name: "Karnataka", kind: "state", stateId: null }, { id: "mumbai", name: "Mumbai", kind: "city", stateId: "IN-MH" }];
export const event: components["schemas"]["FestivalView"] = { id: "event-1", name: "Diwali", startsOn: "2026-11-08", endsOn: "2026-11-08", regions: [], origin: "seeded", rules: 1 };
